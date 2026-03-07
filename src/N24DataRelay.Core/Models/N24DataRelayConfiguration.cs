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
}

public class BrandingSettings
{
    public string CompanyName { get; set; } = "Network24";
    public string ProductName { get; set; } = "N24 Data Relay";
    public string SiteName { get; set; } = "Your Site Name";
    public string SupportEmail { get; set; } = "support@example.com";
    public string? LogoPath { get; set; }
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
    public bool DeleteAfterTransfer { get; set; } = false;
    public bool ArchiveAfterTransfer { get; set; } = true;
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
    public List<string> BlockedFileExtensions { get; set; } = new() { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js" };
    public bool EnableUploadToTransfer { get; set; } = true;
    public KestrelSettings Kestrel { get; set; } = new();
}

public class AuthenticationSettings
{
    public bool EnableEntraId { get; set; } = false;
    public bool EnableLocalAccounts { get; set; } = true;
    public string ConnectionString { get; set; } = "Data Source=/var/lib/n24-data-relay/n24datarelay.db";
    public bool RequireEmailConfirmation { get; set; } = false;
    public bool RequireApproval { get; set; } = true;
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
