using System.IO;

namespace N24DataRelay.Core.Constants;

/// <summary>Application-wide constants for N24 Data Relay (Linux).</summary>
public static class ApplicationConstants
{
    public const string ApplicationName = "N24DataRelay";
    public const string DisplayName = "N24 Data Relay";
    public const string CompanyName = "Network24";
    public const string Version = "0.2.0";

    public static class Configuration
    {
        public const string SectionName = "N24DataRelay";
        public const string DefaultConfigFileName = "appsettings.json";
        /// <summary>Linux FHS config directory. Override with N24_DATA_RELAY_CONFIG_DIR env var.</summary>
        public static string DefaultConfigDirectory =>
            Environment.GetEnvironmentVariable("N24_DATA_RELAY_CONFIG_DIR") ?? "/etc/n24-data-relay";

        /// <summary>Path to shared config file. Prefers /etc when directory exists for production Linux.</summary>
        public static string SharedConfigPath
        {
            get
            {
                var dir = DefaultConfigDirectory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return Path.Combine(dir, DefaultConfigFileName);
                return Path.Combine(dir, DefaultConfigFileName);
            }
        }
    }

    public static class Paths
    {
        public const string DefaultUploadDirectory = "/var/lib/n24-data-relay/uploads";
        public const string DefaultLogDirectory = "/var/log/n24-data-relay";
        public const string DefaultArchiveDirectory = "/var/lib/n24-data-relay/archive";
        public const string DefaultTempDirectory = "/var/lib/n24-data-relay/temp";
    }

    public static class Service
    {
        public const string ServiceName = "n24-data-relay";
        public const string DisplayName = "N24 Data Relay";
        public const string Description = "Automated file transfer from DMZ to SCADA networks";
    }

    public static class Transfer
    {
        public const string MethodSsh = "ssh";
        public const string MethodSmb = "smb";
        public const int DefaultSshPort = 22;
        public const int DefaultConnectionTimeout = 30;
        public const int DefaultOperationTimeout = 300;
    }

    public static class Security
    {
        public const string SshKeyDirectory = "ssh";
        public const string DefaultPrivateKeyName = "id_ed25519";
        public const string DefaultPublicKeyName = "id_ed25519.pub";
        public const string EnvSshKeyPassphrase = "N24_SSH_KEY_PASSPHRASE";
        /// <summary>
        /// Sentinel prefix written by <c>ConfigWriterService</c> before a Data Protection
        /// ciphertext payload stored in a <c>PasswordEncrypted</c> config field.
        /// Readers must strip this prefix before passing the remainder to <c>IDataProtector.Unprotect()</c>.
        /// </summary>
        public const string EncryptedValuePrefix = "ENC:";
    }

    public static class Logging
    {
        public const string DefaultLogFileName = "log-.txt";
        public const string AuditLogFileName = "audit-.txt";
        public const int DefaultRetentionDays = 30;
        public const int DefaultMaxFileSizeMB = 100;
    }
}
