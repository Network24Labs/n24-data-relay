using Microsoft.Extensions.Configuration;
using N24DataRelay.Core.Constants;
using N24DataRelay.Core.Interfaces;

namespace N24DataRelay.Core.Services;

/// <summary>Linux-friendly credential provider: config (e.g. PrivateKeyPassphrase) or env var (N24_SSH_KEY_PASSPHRASE).</summary>
public sealed class ConfigOrEnvCredentialProvider : ICredentialProvider
{
    private readonly IConfiguration _configuration;

    public ConfigOrEnvCredentialProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string? GetCredential(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (string.Equals(key, "SshKeyPassphrase", StringComparison.OrdinalIgnoreCase))
        {
            var fromEnv = Environment.GetEnvironmentVariable(ApplicationConstants.Security.EnvSshKeyPassphrase);
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
            var fromConfig = _configuration.GetSection(ApplicationConstants.Configuration.SectionName).GetSection("Transfer:Ssh")["PrivateKeyPassphrase"];
            return fromConfig;
        }

        return _configuration[$"N24DataRelay:Secrets:{key}"]
            ?? Environment.GetEnvironmentVariable($"N24_{key.ToUpperInvariant().Replace(".", "_")}");
    }

    public bool HasCredential(string key)
    {
        return !string.IsNullOrEmpty(GetCredential(key));
    }
}
