using System.Text.Json;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Reads and writes top-level config sections (N24DataRelay and AzureAd) to the
/// appropriate config file.
///   Development  → {ContentRoot}/appsettings.Development.json
///   Production   → /etc/n24-data-relay/appsettings.json  (or N24_DATA_RELAY_CONFIG_DIR)
///
/// Both files are loaded with reloadOnChange:true, so most saved values take effect
/// immediately via IOptionsMonitor without a restart (port/TLS changes need a restart).
/// </summary>
public class ConfigWriterService
{
    public string WritePath { get; }

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    public ConfigWriterService(IWebHostEnvironment env)
    {
        WritePath = ResolveWritePath(env);
    }

    private static string ResolveWritePath(IWebHostEnvironment env)
    {
        var dir = Environment.GetEnvironmentVariable("N24_DATA_RELAY_CONFIG_DIR");
        if (!string.IsNullOrEmpty(dir))
            return Path.Combine(dir, "appsettings.json");

        if (env.IsDevelopment())
            return Path.Combine(env.ContentRootPath, "appsettings.Development.json");

        return "/etc/n24-data-relay/appsettings.json";
    }

    /// <summary>
    /// Clones the given live config (from IOptionsMonitor) into a mutable copy
    /// that can be modified and written back.
    /// </summary>
    public static N24DataRelayConfiguration Clone(N24DataRelayConfiguration source)
        => JsonSerializer.Deserialize<N24DataRelayConfiguration>(
               JsonSerializer.Serialize(source), ReadOpts)
           ?? new N24DataRelayConfiguration();

    // ── Public write methods ─────────────────────────────────────────────────

    /// <summary>
    /// Replaces the <c>N24DataRelay</c> top-level key, preserving all other keys.
    /// </summary>
    public Task WriteAsync(N24DataRelayConfiguration config)
        => MergeTopLevelAsync("N24DataRelay", config, keyToPreserve: null);

    /// <summary>
    /// Replaces the <c>AzureAd</c> top-level key, preserving all other keys.
    /// </summary>
    /// <param name="instance">Azure AD authority instance, e.g. <c>https://login.microsoftonline.com/</c>.</param>
    /// <param name="tenantId">Azure AD tenant ID (GUID or domain).</param>
    /// <param name="clientId">Application (client) ID of the registered app.</param>
    /// <param name="callbackPath">OIDC redirect path, e.g. <c>/signin-oidc</c>.</param>
    /// <param name="credentialMode">
    ///   <c>"Secret"</c> — writes a <c>ClientSecret</c> field (blank = keep existing secret from file).<br/>
    ///   <c>"ManagedIdentity"</c> — writes a <c>ClientCredentials</c> array with
    ///   <c>SignedAssertionFromManagedIdentity</c>; removes any stored secret.
    /// </param>
    /// <param name="clientSecret">Used only when <paramref name="credentialMode"/> is <c>"Secret"</c>.
    ///   Pass null or empty to preserve the value already in the file.</param>
    /// <param name="managedIdentityClientId">Optional user-assigned managed identity client ID.
    ///   Leave null/empty for system-assigned identity.</param>
    public async Task WriteAzureAdAsync(
        string instance,
        string tenantId,
        string clientId,
        string callbackPath,
        string credentialMode,
        string? clientSecret = null,
        string? managedIdentityClientId = null)
    {
        object section;

        if (credentialMode == "ManagedIdentity")
        {
            // Build the ClientCredentials entry
            object credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? new { SourceType = "SignedAssertionFromManagedIdentity" }
                : new { SourceType = "SignedAssertionFromManagedIdentity", ManagedIdentityClientId = managedIdentityClientId };

            section = new
            {
                Instance           = instance,
                TenantId           = tenantId,
                ClientId           = clientId,
                CallbackPath       = callbackPath,
                ClientCredentials  = new[] { credential }
                // No ClientSecret field — removed intentionally
            };
        }
        else
        {
            // Secret mode: preserve existing file secret when blank
            if (string.IsNullOrEmpty(clientSecret))
                clientSecret = await ReadTopLevelStringAsync("AzureAd", "ClientSecret");

            section = new
            {
                Instance     = instance,
                TenantId     = tenantId,
                ClientId     = clientId,
                ClientSecret = clientSecret,
                CallbackPath = callbackPath
                // No ClientCredentials field
            };
        }

        await MergeTopLevelAsync("AzureAd", section, keyToPreserve: null);
    }

    /// <summary>
    /// Reads a single string value from a top-level section in the write-target file.
    /// Returns null if the file, section, or property is missing.
    /// </summary>
    public async Task<string?> ReadTopLevelStringAsync(string section, string property)
    {
        if (!File.Exists(WritePath)) return null;
        try
        {
            var raw = await File.ReadAllTextAsync(WritePath);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty(section, out var sec) &&
                sec.TryGetProperty(property, out var val))
                return val.GetString();
        }
        catch { }
        return null;
    }

    // ── Shared implementation ────────────────────────────────────────────────

    /// <summary>
    /// Reads the current file, replaces <paramref name="key"/> with <paramref name="value"/>,
    /// and writes back. All other top-level keys are preserved in their original order.
    /// </summary>
    private async Task MergeTopLevelAsync(string key, object value, string? keyToPreserve)
    {
        var dir = Path.GetDirectoryName(WritePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var preserved = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(WritePath))
        {
            try
            {
                var raw = await File.ReadAllTextAsync(WritePath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
                if (parsed != null)
                    foreach (var kv in parsed)
                        if (!kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                            preserved[kv.Key] = kv.Value;
            }
            catch { /* stale/invalid file — start fresh */ }
        }

        using var ms = new MemoryStream();
        await using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var kv in preserved)
        {
            writer.WritePropertyName(kv.Key);
            kv.Value.WriteTo(writer);
        }
        writer.WritePropertyName(key);
        JsonSerializer.Serialize(writer, value, WriteOpts);
        writer.WriteEndObject();
        await writer.FlushAsync();

        await File.WriteAllTextAsync(WritePath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
