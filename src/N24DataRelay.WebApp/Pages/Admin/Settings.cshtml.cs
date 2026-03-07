using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SettingsModel : PageModel
{
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly ConfigWriterService _writer;
    private readonly IConfiguration _rawConfig;

    public SettingsModel(
        IOptionsMonitor<N24DataRelayConfiguration> config,
        ConfigWriterService writer,
        IConfiguration rawConfig)
    {
        _config = config;
        _writer = writer;
        _rawConfig = rawConfig;
    }

    public string WritePath => _writer.WritePath;
    public N24DataRelayConfiguration Config => _config.CurrentValue;

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ActiveTab { get; set; }

    // ── Bound sections ──────────────────────────────────────────────────────
    [BindProperty] public SshInput Ssh { get; set; } = new();
    [BindProperty] public ServiceInput Service { get; set; } = new();
    [BindProperty] public PortalInput Portal { get; set; } = new();
    [BindProperty] public BrandingInput Branding { get; set; } = new();

    // ── Input models ────────────────────────────────────────────────────────
    public class SshInput
    {
        [Required] public string Host { get; set; } = "";
        [Range(1, 65535)] public int Port { get; set; } = 22;
        [Required] public string Username { get; set; } = "";
        public string AuthMethod { get; set; } = "PublicKey";
        public string? PrivateKeyPath { get; set; }
        [Required] public string DestinationPath { get; set; } = "";
        public string RemoteServerType { get; set; } = "Linux";
        public bool Compression { get; set; } = true;
        [Range(10, 300)] public int ConnectionTimeout { get; set; } = 30;
        [Range(60, 3600)] public int OperationTimeout { get; set; } = 300;
        public bool StrictHostKeyChecking { get; set; } = true;
    }

    public class ServiceInput
    {
        [Required] public string WatchDirectory { get; set; } = "";
        public string TransferMethod { get; set; } = "ssh";
        public bool DeleteAfterTransfer { get; set; } = true;
        public bool ArchiveAfterTransfer { get; set; } = false;
        public string? ArchiveDirectory { get; set; }
        public bool VerifyTransfer { get; set; } = true;
        [Range(0, 10)] public int RetryAttempts { get; set; } = 3;
        [Range(5, 300)] public int RetryDelaySeconds { get; set; } = 30;
        [Range(1.0, 5.0)] public double RetryBackoffMultiplier { get; set; } = 2.0;
        [Range(1, 60)] public int FileStabilitySeconds { get; set; } = 5;
        [Range(5, 300)] public int ProcessingIntervalSeconds { get; set; } = 10;
    }

    public class PortalInput
    {
        [Range(1, 65535)] public int HttpPort { get; set; } = 8080;
        [Range(1, 65535)] public int HttpsPort { get; set; } = 8443;
        public bool EnableHttps { get; set; }
        public string? CertificatePath { get; set; }
        /// <summary>Max upload size expressed in GB for the UI; converted to/from bytes on load/save.</summary>
        [Range(0.1, 100.0)] public double MaxFileSizeGb { get; set; } = 4.0;
        /// <summary>Comma-separated blocked extensions, e.g. ".exe,.dll"</summary>
        public string BlockedExtensions { get; set; } = "";
        public bool EnableUploadToTransfer { get; set; } = true;
        [Required] public string UploadDirectory { get; set; } = "";
        public bool EnableLocalAccounts { get; set; } = true;
        public bool RequireApproval { get; set; } = true;

        // ── Entra ID (Azure AD) ──────────────────────────────────────────────
        public bool EnableEntraId { get; set; }
        public string EntraIdInstance { get; set; } = "https://login.microsoftonline.com/";
        public string EntraIdTenantId { get; set; } = "";
        public string EntraIdClientId { get; set; } = "";
        /// <summary>Leave blank to keep the existing secret. Written to config file if provided.</summary>
        public string? EntraIdClientSecret { get; set; }
        public string EntraIdCallbackPath { get; set; } = "/signin-oidc";
        /// <summary>"Secret" (default) or "ManagedIdentity" (Azure Arc workload identity).</summary>
        public string EntraIdCredentialMode { get; set; } = "Secret";
        /// <summary>Optional user-assigned managed identity client ID. Leave blank for system-assigned.</summary>
        public string? ManagedIdentityClientId { get; set; }
    }

    /// <summary>
    /// Status of the Entra ID client secret: from file, from environment variable, or not set.
    /// </summary>
    public string EntraIdSecretStatus { get; private set; } = "not set";

    public class BrandingInput
    {
        public string CompanyName { get; set; } = "";
        public string SiteName { get; set; } = "";
        [EmailAddress] public string SupportEmail { get; set; } = "";
        public string PrimaryColor { get; set; } = "#0066CC";
    }

    // ── GET: populate forms from current live config ─────────────────────────
    public void OnGet()
    {
        PopulateFromConfig(_config.CurrentValue);
        SetEntraIdSecretStatus();
    }

    private void SetEntraIdSecretStatus()
    {
        // Managed identity mode has no secret
        if (_rawConfig["AzureAd:ClientCredentials:0:SourceType"] == "SignedAssertionFromManagedIdentity")
        {
            EntraIdSecretStatus = "managed identity";
            return;
        }

        var secret = _rawConfig["AzureAd:ClientSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            EntraIdSecretStatus = "not set";
            return;
        }
        var envSecret = Environment.GetEnvironmentVariable("AzureAd__ClientSecret")
                     ?? Environment.GetEnvironmentVariable("AZUREAD__CLIENTSECRET");
        EntraIdSecretStatus = !string.IsNullOrEmpty(envSecret) ? "set via environment variable" : "set in config file";
    }

    private void PopulateFromConfig(N24DataRelayConfiguration c)
    {
        Ssh = new SshInput
        {
            Host = c.Transfer.Ssh.Host,
            Port = c.Transfer.Ssh.Port,
            Username = c.Transfer.Ssh.Username,
            AuthMethod = c.Transfer.Ssh.AuthMethod,
            PrivateKeyPath = c.Transfer.Ssh.PrivateKeyPath,
            DestinationPath = c.Transfer.Ssh.DestinationPath,
            RemoteServerType = c.Transfer.Ssh.RemoteServerType,
            Compression = c.Transfer.Ssh.Compression,
            ConnectionTimeout = c.Transfer.Ssh.ConnectionTimeout,
            OperationTimeout = c.Transfer.Ssh.OperationTimeout,
            StrictHostKeyChecking = c.Transfer.Ssh.StrictHostKeyChecking
        };
        Service = new ServiceInput
        {
            WatchDirectory = c.Service.WatchDirectory,
            TransferMethod = c.Service.TransferMethod,
            DeleteAfterTransfer = c.Service.DeleteAfterTransfer,
            ArchiveAfterTransfer = c.Service.ArchiveAfterTransfer,
            ArchiveDirectory = c.Service.ArchiveDirectory,
            VerifyTransfer = c.Service.VerifyTransfer,
            RetryAttempts = c.Service.RetryAttempts,
            RetryDelaySeconds = c.Service.RetryDelaySeconds,
            RetryBackoffMultiplier = c.Service.RetryBackoffMultiplier,
            FileStabilitySeconds = c.Service.FileStabilitySeconds,
            ProcessingIntervalSeconds = c.Service.ProcessingIntervalSeconds
        };
        Portal = new PortalInput
        {
            HttpPort = c.WebPortal.Kestrel.HttpPort,
            HttpsPort = c.WebPortal.Kestrel.HttpsPort,
            EnableHttps = c.WebPortal.Kestrel.EnableHttps,
            CertificatePath = c.WebPortal.Kestrel.CertificatePath,
            MaxFileSizeGb = Math.Round(c.WebPortal.MaxFileSizeBytes / (1024.0 * 1024.0 * 1024.0), 2),
            BlockedExtensions = string.Join(", ", c.WebPortal.BlockedFileExtensions ?? new()),
            EnableUploadToTransfer = c.WebPortal.EnableUploadToTransfer,
            UploadDirectory = c.Paths.UploadDirectory,
            EnableLocalAccounts = c.WebPortal.Authentication.EnableLocalAccounts,
            RequireApproval = c.WebPortal.Authentication.RequireApproval,
            EnableEntraId = c.WebPortal.Authentication.EnableEntraId,
            // Entra ID credentials from raw IConfiguration (top-level AzureAd section)
            EntraIdInstance          = _rawConfig["AzureAd:Instance"]     ?? "https://login.microsoftonline.com/",
            EntraIdTenantId          = _rawConfig["AzureAd:TenantId"]     ?? "",
            EntraIdClientId          = _rawConfig["AzureAd:ClientId"]     ?? "",
            EntraIdClientSecret      = null, // Never pre-fill the secret field
            EntraIdCallbackPath      = _rawConfig["AzureAd:CallbackPath"] ?? "/signin-oidc",
            // Detect credential mode from current config
            EntraIdCredentialMode    = _rawConfig["AzureAd:ClientCredentials:0:SourceType"]
                                       == "SignedAssertionFromManagedIdentity"
                                       ? "ManagedIdentity" : "Secret",
            ManagedIdentityClientId  = _rawConfig["AzureAd:ClientCredentials:0:ManagedIdentityClientId"]
        };
        Branding = new BrandingInput
        {
            CompanyName = c.Branding.CompanyName,
            SiteName = c.Branding.SiteName,
            SupportEmail = c.Branding.SupportEmail,
            PrimaryColor = c.Branding.Theme.PrimaryColor
        };
    }

    // ── POST handlers — one per tab ──────────────────────────────────────────

    public async Task<IActionResult> OnPostSaveSshAsync()
    {
        KeepOnly("Ssh");
        if (!ModelState.IsValid)
        {
            ActiveTab = "ssh";
            PopulateOtherSections("ssh");
            return Page();
        }

        var cfg = ConfigWriterService.Clone(_config.CurrentValue);
        cfg.Transfer.Ssh.Host = Ssh.Host.Trim();
        cfg.Transfer.Ssh.Port = Ssh.Port;
        cfg.Transfer.Ssh.Username = Ssh.Username.Trim();
        cfg.Transfer.Ssh.AuthMethod = Ssh.AuthMethod;
        cfg.Transfer.Ssh.PrivateKeyPath = Ssh.PrivateKeyPath?.Trim();
        cfg.Transfer.Ssh.DestinationPath = Ssh.DestinationPath.Trim();
        cfg.Transfer.Ssh.RemoteServerType = Ssh.RemoteServerType;
        cfg.Transfer.Ssh.Compression = Ssh.Compression;
        cfg.Transfer.Ssh.ConnectionTimeout = Ssh.ConnectionTimeout;
        cfg.Transfer.Ssh.OperationTimeout = Ssh.OperationTimeout;
        cfg.Transfer.Ssh.StrictHostKeyChecking = Ssh.StrictHostKeyChecking;

        await _writer.WriteAsync(cfg);
        StatusMessage = "SSH settings saved.";
        ActiveTab = "ssh";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveServiceAsync()
    {
        KeepOnly("Service");
        if (!ModelState.IsValid)
        {
            ActiveTab = "service";
            PopulateOtherSections("service");
            return Page();
        }

        var cfg = ConfigWriterService.Clone(_config.CurrentValue);
        cfg.Service.WatchDirectory = Service.WatchDirectory.Trim();
        cfg.Service.TransferMethod = Service.TransferMethod;
        cfg.Service.DeleteAfterTransfer = Service.DeleteAfterTransfer;
        cfg.Service.ArchiveAfterTransfer = Service.ArchiveAfterTransfer;
        cfg.Service.ArchiveDirectory = Service.ArchiveDirectory?.Trim() ?? cfg.Service.ArchiveDirectory;
        cfg.Service.VerifyTransfer = Service.VerifyTransfer;
        cfg.Service.RetryAttempts = Service.RetryAttempts;
        cfg.Service.RetryDelaySeconds = Service.RetryDelaySeconds;
        cfg.Service.RetryBackoffMultiplier = Service.RetryBackoffMultiplier;
        cfg.Service.FileStabilitySeconds = Service.FileStabilitySeconds;
        cfg.Service.ProcessingIntervalSeconds = Service.ProcessingIntervalSeconds;

        await _writer.WriteAsync(cfg);
        StatusMessage = "Transfer service settings saved.";
        ActiveTab = "service";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSavePortalAsync()
    {
        KeepOnly("Portal");
        if (!ModelState.IsValid)
        {
            ActiveTab = "portal";
            PopulateOtherSections("portal");
            SetEntraIdSecretStatus();
            return Page();
        }

        var cfg = ConfigWriterService.Clone(_config.CurrentValue);
        cfg.WebPortal.Kestrel.HttpPort = Portal.HttpPort;
        cfg.WebPortal.Kestrel.HttpsPort = Portal.HttpsPort;
        cfg.WebPortal.Kestrel.EnableHttps = Portal.EnableHttps;
        cfg.WebPortal.Kestrel.CertificatePath = Portal.CertificatePath?.Trim();
        cfg.WebPortal.MaxFileSizeBytes = (long)(Portal.MaxFileSizeGb * 1024 * 1024 * 1024);
        cfg.WebPortal.BlockedFileExtensions = Portal.BlockedExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.StartsWith('.'))
            .ToList();
        cfg.WebPortal.EnableUploadToTransfer = Portal.EnableUploadToTransfer;
        cfg.Paths.UploadDirectory = Portal.UploadDirectory.Trim();
        cfg.WebPortal.Authentication.EnableLocalAccounts = Portal.EnableLocalAccounts;
        cfg.WebPortal.Authentication.RequireApproval = Portal.RequireApproval;
        cfg.WebPortal.Authentication.EnableEntraId = Portal.EnableEntraId;

        await _writer.WriteAsync(cfg);

        // Save Entra ID credentials to the top-level AzureAd config section
        await _writer.WriteAzureAdAsync(
            instance:                Portal.EntraIdInstance.Trim(),
            tenantId:                Portal.EntraIdTenantId.Trim(),
            clientId:                Portal.EntraIdClientId.Trim(),
            callbackPath:            Portal.EntraIdCallbackPath.Trim(),
            credentialMode:          Portal.EntraIdCredentialMode,
            clientSecret:            Portal.EntraIdClientSecret,          // null/blank = keep existing
            managedIdentityClientId: Portal.ManagedIdentityClientId?.Trim());

        StatusMessage = "Web portal settings saved. Port changes require a restart.";
        ActiveTab = "portal";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveBrandingAsync()
    {
        KeepOnly("Branding");
        if (!ModelState.IsValid)
        {
            ActiveTab = "branding";
            PopulateOtherSections("branding");
            return Page();
        }

        var cfg = ConfigWriterService.Clone(_config.CurrentValue);
        cfg.Branding.CompanyName = Branding.CompanyName.Trim();
        cfg.Branding.SiteName = Branding.SiteName.Trim();
        cfg.Branding.SupportEmail = Branding.SupportEmail.Trim();
        cfg.Branding.Theme.PrimaryColor = Branding.PrimaryColor;

        await _writer.WriteAsync(cfg);
        StatusMessage = "Branding settings saved.";
        ActiveTab = "branding";
        return RedirectToPage();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Remove model-state entries for all sections except the active one.</summary>
    private void KeepOnly(string section)
    {
        foreach (var key in ModelState.Keys.ToList())
            if (!key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase) && key != "")
                ModelState.Remove(key);
    }

    /// <summary>Repopulate non-submitted sections from live config so the page renders correctly on validation failure.</summary>
    private void PopulateOtherSections(string active)
    {
        var c = _config.CurrentValue;
        if (active != "ssh") Ssh = new SshInput { Host = c.Transfer.Ssh.Host, Port = c.Transfer.Ssh.Port, Username = c.Transfer.Ssh.Username, AuthMethod = c.Transfer.Ssh.AuthMethod, PrivateKeyPath = c.Transfer.Ssh.PrivateKeyPath, DestinationPath = c.Transfer.Ssh.DestinationPath, RemoteServerType = c.Transfer.Ssh.RemoteServerType, Compression = c.Transfer.Ssh.Compression, ConnectionTimeout = c.Transfer.Ssh.ConnectionTimeout, OperationTimeout = c.Transfer.Ssh.OperationTimeout, StrictHostKeyChecking = c.Transfer.Ssh.StrictHostKeyChecking };
        if (active != "service") Service = new ServiceInput { WatchDirectory = c.Service.WatchDirectory, TransferMethod = c.Service.TransferMethod, DeleteAfterTransfer = c.Service.DeleteAfterTransfer, ArchiveAfterTransfer = c.Service.ArchiveAfterTransfer, ArchiveDirectory = c.Service.ArchiveDirectory, VerifyTransfer = c.Service.VerifyTransfer, RetryAttempts = c.Service.RetryAttempts, RetryDelaySeconds = c.Service.RetryDelaySeconds, RetryBackoffMultiplier = c.Service.RetryBackoffMultiplier, FileStabilitySeconds = c.Service.FileStabilitySeconds, ProcessingIntervalSeconds = c.Service.ProcessingIntervalSeconds };
        if (active != "portal") Portal = new PortalInput { HttpPort = c.WebPortal.Kestrel.HttpPort, HttpsPort = c.WebPortal.Kestrel.HttpsPort, EnableHttps = c.WebPortal.Kestrel.EnableHttps, CertificatePath = c.WebPortal.Kestrel.CertificatePath, MaxFileSizeGb = Math.Round(c.WebPortal.MaxFileSizeBytes / (1024.0 * 1024 * 1024), 2), BlockedExtensions = string.Join(", ", c.WebPortal.BlockedFileExtensions ?? new()), EnableUploadToTransfer = c.WebPortal.EnableUploadToTransfer, UploadDirectory = c.Paths.UploadDirectory, EnableLocalAccounts = c.WebPortal.Authentication.EnableLocalAccounts, RequireApproval = c.WebPortal.Authentication.RequireApproval, EnableEntraId = c.WebPortal.Authentication.EnableEntraId, EntraIdInstance = _rawConfig["AzureAd:Instance"] ?? "https://login.microsoftonline.com/", EntraIdTenantId = _rawConfig["AzureAd:TenantId"] ?? "", EntraIdClientId = _rawConfig["AzureAd:ClientId"] ?? "", EntraIdCallbackPath = _rawConfig["AzureAd:CallbackPath"] ?? "/signin-oidc", EntraIdCredentialMode = _rawConfig["AzureAd:ClientCredentials:0:SourceType"] == "SignedAssertionFromManagedIdentity" ? "ManagedIdentity" : "Secret", ManagedIdentityClientId = _rawConfig["AzureAd:ClientCredentials:0:ManagedIdentityClientId"] };
        if (active != "branding") Branding = new BrandingInput { CompanyName = c.Branding.CompanyName, SiteName = c.Branding.SiteName, SupportEmail = c.Branding.SupportEmail, PrimaryColor = c.Branding.Theme.PrimaryColor };
    }
}
