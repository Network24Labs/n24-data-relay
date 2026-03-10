namespace N24DataRelay.Core.Models;

/// <summary>Root configuration model for N24 Data Relay (Linux).</summary>
public class N24DataRelayConfiguration
{
    public BrandingSettings Branding { get; set; } = new();
    public PathSettings Paths { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public ServiceSettings Service { get; set; } = new();
    public WebPortalSettings WebPortal { get; set; } = new();
    public TransferSettings Transfer { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
}

public class BrandingSettings
{
    public string CompanyName { get; set; } = "Network24";
    public string ProductName { get; set; } = "N24 Data Relay";
    public string SiteName { get; set; } = "Your Site Name";
    public string SupportEmail { get; set; } = "support@example.com";
    public string? LogoPath { get; set; }
    /// <summary>Display name for the local/upload side (e.g. "DMZ", "MPM DMZ"). Shown in UI labels.</summary>
    public string DmzSideName { get; set; } = "DMZ";
    /// <summary>Display name for the transfer destination side (e.g. "SCADA", "MPM SCADA"). Shown in UI labels.</summary>
    public string ScadaSideName { get; set; } = "SCADA";
    public ThemeSettings Theme { get; set; } = new();
}

public class ThemeSettings
{
    public string PrimaryColor { get; set; } = "#0066CC";
    public string SecondaryColor { get; set; } = "#003366";
    public string AccentColor { get; set; } = "#FF6600";
}

public class PathSettings
{
    public string UploadDirectory { get; set; } = "/var/lib/n24-data-relay/uploads";
    public string TransferDirectory { get; set; } = "/var/lib/n24-data-relay/uploads/transfer";
    public string LogDirectory { get; set; } = "/var/log/n24-data-relay";
    public string ConfigDirectory { get; set; } = "/etc/n24-data-relay";
    public string TempDirectory { get; set; } = "/var/lib/n24-data-relay/temp";
}

public class LoggingSettings
{
    public int RetentionDays { get; set; } = 30;
    public int MaxFileSizeMB { get; set; } = 100;
    public bool EnableEventLog { get; set; } = false;
    public bool EnableConsole { get; set; } = true;
}

public class ServiceSettings
{
    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "n24-data-relay";
    public string DisplayName { get; set; } = "N24 Data Relay Watcher";
    public string Description { get; set; } = "Automated file transfer from DMZ to SCADA networks";
    public string WatchDirectory { get; set; } = "/var/lib/n24-data-relay/uploads/transfer";
    public string TransferMethod { get; set; } = "ssh";
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int MaxConcurrentTransfers { get; set; } = 5;
    public string FileFilter { get; set; } = "*.*";
    public bool DeleteAfterTransfer { get; set; } = true;
    public bool ArchiveAfterTransfer { get; set; } = false;
    public string ArchiveDirectory { get; set; } = "/var/lib/n24-data-relay/archive";
    public bool VerifyTransfer { get; set; } = true;
    public int FileStabilitySeconds { get; set; } = 5;
    public int ProcessingIntervalSeconds { get; set; } = 10;
    public int MaxQueueSize { get; set; } = 10000;
    public bool IncludeSubdirectories { get; set; } = true;
}

public class WebPortalSettings
{
    public bool Enabled { get; set; } = true;
    public AuthenticationSettings Authentication { get; set; } = new();
    public long MaxFileSizeBytes { get; set; } = 4294967295;
    public int MaxConcurrentUploads { get; set; } = 10;
    public List<string> BlockedFileExtensions { get; set; } = new();
    public bool EnableUploadToTransfer { get; set; } = true;
    public KestrelSettings Kestrel { get; set; } = new();
}

public class AuthenticationSettings
{
    public bool EnableEntraId { get; set; } = false;
    public bool EnableLocalAccounts { get; set; } = true;
    /// <summary>When false the <c>/Register</c> page is disabled; existing local accounts can still sign in.</summary>
    public bool EnableSelfRegistration { get; set; } = true;
    public bool EnableLdap { get; set; } = false;
    public LdapSettings Ldap { get; set; } = new();
    /// <summary>Which sign-in form is shown by default on the login page: <c>"Local"</c>, <c>"Ldap"</c>, or <c>"EntraId"</c>.</summary>
    public string DefaultLoginMethod { get; set; } = "Local";
    public string ConnectionString { get; set; } = "Data Source=/var/lib/n24-data-relay/n24datarelay.db";
    public bool RequireEmailConfirmation { get; set; } = false;
    /// <summary>Require admin approval for new <b>local</b> accounts. Directory-backed users (Entra/LDAP) are auto-approved.</summary>
    public bool RequireApproval { get; set; } = true;
    /// <summary>Number of days before a local-account password expires. 0 = never expires.</summary>
    public int PasswordExpiryDays { get; set; } = 90;
    /// <summary>Days before expiry at which a warning banner is shown. 0 = no warning.</summary>
    public int PasswordExpiryWarningDays { get; set; } = 14;
    /// <summary>
    /// Bearer token required for <c>/api/v1</c> endpoints. Generate a random 32+ char string.
    /// Prefer the <c>N24DataRelay__WebPortal__Authentication__ApiKey</c> environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class LdapSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = false;
    public bool StartTls { get; set; } = false;
    /// <summary>Base DN for the user search, e.g. <c>DC=corp,DC=example,DC=com</c>.</summary>
    public string BaseDn { get; set; } = string.Empty;
    /// <summary>Service account DN used for the initial bind and user search. Leave empty for anonymous bind.</summary>
    public string BindDn { get; set; } = string.Empty;
    /// <summary>
    /// Service account password, encrypted at rest by Data Protection.
    /// For production prefer <see cref="BindPasswordFile"/> or the
    /// <c>N24_LDAP_BIND_PASSWORD</c> environment variable instead.
    /// </summary>
    public string BindPassword { get; set; } = string.Empty;
    /// <summary>
    /// Absolute path to a file whose first line contains the bind password.
    /// Recommended for production — works with systemd <c>LoadCredential=</c>,
    /// Docker/Podman secrets, and Kubernetes secret mounts.
    /// Takes priority over <see cref="BindPassword"/> and the environment variable.
    /// </summary>
    public string BindPasswordFile { get; set; } = string.Empty;
    /// <summary>
    /// LDAP search filter with <c>{0}</c> placeholder for the login input.
    /// AD default: <c>(&amp;(objectClass=user)(sAMAccountName={0}))</c>.
    /// OpenLDAP: <c>(&amp;(objectClass=inetOrgPerson)(uid={0}))</c>.
    /// </summary>
    public string UserSearchFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";
    /// <summary>LDAP attribute containing the user's email address.</summary>
    public string EmailAttribute { get; set; } = "mail";
    /// <summary>LDAP attribute containing the user's display name.</summary>
    public string DisplayNameAttribute { get; set; } = "displayName";
    /// <summary>Domain prefix shown in the login placeholder, e.g. <c>CORP</c>.</summary>
    public string DomainHint { get; set; } = string.Empty;
    /// <summary>
    /// DN or common name of a security group the user must be a member of.
    /// Leave empty to allow any authenticated AD user.
    /// </summary>
    public string RequiredGroup { get; set; } = string.Empty;
    public int ConnectionTimeoutSeconds { get; set; } = 10;
}

public class KestrelSettings
{
    public int HttpPort { get; set; } = 8080;
    public int HttpsPort { get; set; } = 8443;
    public bool EnableHttps { get; set; } = false;
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}

public class TransferSettings
{
    public SshSettings Ssh { get; set; } = new();
    public SmbSettings Smb { get; set; } = new();
}

public class SshSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = "PublicKey";
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string? PasswordEncrypted { get; set; }
    public string DestinationPath { get; set; } = string.Empty;
    public string RemoteServerType { get; set; } = "Linux";
    public bool CreateDestinationDirectory { get; set; } = true;
    public bool PreserveTimestamps { get; set; } = true;
    public int ConnectionTimeout { get; set; } = 30;
    public int OperationTimeout { get; set; } = 300;
    public int TransferTimeout { get; set; } = 300;
    public int KeepAliveInterval { get; set; } = 30;
    public bool Compression { get; set; } = true;
    public bool StrictHostKeyChecking { get; set; } = true;
    /// <summary>
    /// Expected SHA-256 fingerprint of the remote server's host key (hex, with or without colons).
    /// Required when <see cref="StrictHostKeyChecking"/> is true. Leave empty to disable verification
    /// (connection is rejected when strict checking is on and this is unset).
    /// Obtain via: <c>ssh-keyscan -p PORT HOST | ssh-keygen -lf - -E sha256</c>
    /// </summary>
    public string? KnownHostFingerprint { get; set; }
}

public class SmtpSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    /// <summary>"None" | "StartTls" | "Ssl" — maps to MailKit SecureSocketOptions.</summary>
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = string.Empty;
    /// <summary>
    /// SMTP password. Prefer the <c>N24DataRelay__Smtp__Password</c> environment variable
    /// so the secret is never written to the config file.
    /// </summary>
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "N24 Data Relay";
    public int TimeoutSeconds { get; set; } = 30;
}

public class SmbSettings
{
    public string Server { get; set; } = string.Empty;
    public string SharePath { get; set; } = string.Empty;
    public bool UseCredentials { get; set; } = false;
    public string? Username { get; set; }
    public string? PasswordEncrypted { get; set; }
    public string? Domain { get; set; }
    public int Timeout { get; set; } = 300;
}
