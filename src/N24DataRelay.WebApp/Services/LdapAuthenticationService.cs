using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Models;
using Novell.Directory.Ldap;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Authenticates users against an AD/LDAP directory. The flow is:
///   1. Connect to the LDAP server (optionally with SSL/StartTLS).
///   2. Bind with a service account and search for the user entry.
///   3. Re-bind with the user's own DN + password to verify credentials.
///   4. Optionally verify the user is a member of a required security group.
/// </summary>
public class LdapAuthenticationService
{
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(
        IOptionsMonitor<N24DataRelayConfiguration> config,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<LdapAuthenticationService> logger)
    {
        _config = config;
        _dataProtector = dataProtectionProvider.CreateProtector("N24DataRelay.Credentials");
        _logger = logger;
    }

    public record LdapUserInfo(string Dn, string Email, string? DisplayName);

    public record LdapAuthResult(bool Success, string? ErrorMessage = null, LdapUserInfo? User = null);

    /// <summary>Authenticate a user against the configured LDAP directory.</summary>
    public async Task<LdapAuthResult> AuthenticateAsync(string username, string password)
    {
        var ldap = _config.CurrentValue.WebPortal.Authentication.Ldap;

        if (string.IsNullOrWhiteSpace(ldap.Host))
            return new LdapAuthResult(false, "LDAP server is not configured.");

        try
        {
            using var connection = new LdapConnection();
            await ConnectAsync(connection, ldap);

            // Step 1: Service-account bind + search for the user entry
            await BindServiceAccountAsync(connection, ldap);
            var entry = await SearchUserAsync(connection, ldap, username);
            if (entry == null)
                return new LdapAuthResult(false, "Invalid username or password.");

            var userDn = entry.Dn;
            var email = GetOptionalAttribute(entry, ldap.EmailAttribute);
            var displayName = GetOptionalAttribute(entry, ldap.DisplayNameAttribute);

            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("LDAP user {Dn} has no value for email attribute '{Attr}'", userDn, ldap.EmailAttribute);
                return new LdapAuthResult(false, "Your directory account does not have an email address configured.");
            }

            // Step 2: Check required group membership before verifying the password
            if (!string.IsNullOrWhiteSpace(ldap.RequiredGroup))
            {
                if (!IsMemberOfGroup(entry, ldap.RequiredGroup))
                {
                    _logger.LogInformation("LDAP user {Dn} denied: not a member of required group '{Group}'", userDn, ldap.RequiredGroup);
                    return new LdapAuthResult(false, "You are not a member of the required security group.");
                }
            }

            // Step 3: Verify the user's password by binding with their DN
            try
            {
                using var userConn = new LdapConnection();
                await ConnectAsync(userConn, ldap);
                await userConn.BindAsync(userDn, password);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                return new LdapAuthResult(false, "Invalid username or password.");
            }

            _logger.LogInformation("LDAP authentication succeeded for {Email} ({Dn})", email, userDn);
            return new LdapAuthResult(true, User: new LdapUserInfo(userDn, email, displayName));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP authentication failed for user '{Username}': {Message}", username, ex.Message);
            return new LdapAuthResult(false, "Directory service unavailable. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during LDAP authentication for user '{Username}'", username);
            return new LdapAuthResult(false, "An unexpected error occurred. Please try again later.");
        }
    }

    /// <summary>Tests connectivity and service-account bind. Used by the admin settings "Test" button.</summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        var ldap = _config.CurrentValue.WebPortal.Authentication.Ldap;

        if (string.IsNullOrWhiteSpace(ldap.Host))
            return (false, "LDAP host is not configured.");

        try
        {
            using var connection = new LdapConnection();
            await ConnectAsync(connection, ldap);
            await BindServiceAccountAsync(connection, ldap);

            var msg = $"Connected to {ldap.Host}:{ldap.Port}" +
                      (ldap.UseSsl ? " (SSL)" : ldap.StartTls ? " (StartTLS)" : "") +
                      ". Service account bind succeeded." +
                      (!string.IsNullOrWhiteSpace(ldap.BaseDn) ? $" Base DN: {ldap.BaseDn}" : "");
            return (true, msg);
        }
        catch (LdapException ex)
        {
            return (false, $"LDAP error ({ex.ResultCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private static async Task ConnectAsync(LdapConnection connection, LdapSettings ldap)
    {
        connection.ConnectionTimeout = ldap.ConnectionTimeoutSeconds * 1000;

        if (ldap.UseSsl)
            connection.SecureSocketLayer = true;

        await connection.ConnectAsync(ldap.Host, ldap.Port);

        if (ldap.StartTls && !ldap.UseSsl)
            await connection.StartTlsAsync();
    }

    private async Task BindServiceAccountAsync(LdapConnection connection, LdapSettings ldap)
    {
        if (!string.IsNullOrWhiteSpace(ldap.BindDn))
        {
            var bindPassword = ResolveBindPassword(ldap);
            await connection.BindAsync(ldap.BindDn, bindPassword ?? string.Empty);
        }
        else
        {
            await connection.BindAsync(null, (string?)null);
        }
    }

    /// <summary>
    /// Resolves the LDAP bind password from (in priority order):
    ///   1. File path  (BindPasswordFile) — recommended for production
    ///   2. Environment variable (N24_LDAP_BIND_PASSWORD)
    ///   3. Config value (BindPassword), decrypted if stored with the ENC: prefix
    /// </summary>
    private string? ResolveBindPassword(LdapSettings ldap)
    {
        // 1. File-based secret (systemd LoadCredential, Docker/K8s secrets, etc.)
        //    File may contain plaintext or an ENC:-prefixed Data Protection ciphertext.
        if (!string.IsNullOrWhiteSpace(ldap.BindPasswordFile))
        {
            try
            {
                if (File.Exists(ldap.BindPasswordFile))
                {
                    var fileContent = File.ReadAllText(ldap.BindPasswordFile).Trim();
                    var encPrefix = ApplicationConstants.Security.EncryptedValuePrefix;
                    if (fileContent.StartsWith(encPrefix, StringComparison.Ordinal))
                    {
                        try { return _dataProtector.Unprotect(fileContent[encPrefix.Length..]); }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to decrypt LDAP bind password from file '{Path}'", ldap.BindPasswordFile);
                            return null;
                        }
                    }
                    return fileContent;
                }
                _logger.LogWarning("LDAP BindPasswordFile '{Path}' does not exist", ldap.BindPasswordFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read LDAP bind password from file '{Path}'", ldap.BindPasswordFile);
            }
        }

        // 2. Environment variable
        var envPassword = Environment.GetEnvironmentVariable("N24_LDAP_BIND_PASSWORD");
        if (!string.IsNullOrEmpty(envPassword))
            return envPassword;

        // 3. Config value (possibly encrypted with Data Protection)
        if (string.IsNullOrEmpty(ldap.BindPassword))
            return null;

        var prefix = ApplicationConstants.Security.EncryptedValuePrefix;
        if (ldap.BindPassword.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                return _dataProtector.Unprotect(ldap.BindPassword[prefix.Length..]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt LDAP bind password. The data-protection key may have changed.");
                return null;
            }
        }

        return ldap.BindPassword;
    }

    private static async Task<LdapEntry?> SearchUserAsync(LdapConnection connection, LdapSettings ldap, string username)
    {
        var escapedUsername = EscapeLdapFilter(username);
        var filter = ldap.UserSearchFilter.Replace("{0}", escapedUsername);

        var results = await connection.SearchAsync(
            ldap.BaseDn, LdapConnection.ScopeSub, filter,
            new[] { "dn", ldap.EmailAttribute, ldap.DisplayNameAttribute, "memberOf" },
            false);

        if (await results.HasMoreAsync())
            return await results.NextAsync();

        return null;
    }

    private static bool IsMemberOfGroup(LdapEntry entry, string requiredGroup)
    {
        var memberOf = entry.GetAttributeSet();
        if (!memberOf.ContainsKey("memberOf"))
            return false;

        var groupValues = memberOf["memberOf"].StringValueArray;
        var requiredTrimmed = requiredGroup.Trim();

        foreach (var group in groupValues)
        {
            if (string.Equals(group, requiredTrimmed, StringComparison.OrdinalIgnoreCase))
                return true;

            if (group.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                var cn = group.Split(',')[0]["CN=".Length..];
                if (string.Equals(cn, requiredTrimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string? GetOptionalAttribute(LdapEntry entry, string attributeName)
    {
        try
        {
            var attrs = entry.GetAttributeSet();
            return attrs.ContainsKey(attributeName) ? attrs[attributeName].StringValue : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Escapes special characters in an LDAP search filter value per RFC 4515.</summary>
    private static string EscapeLdapFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
