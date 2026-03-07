using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Services;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class EmailModel : PageModel
{
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly ConfigWriterService _writer;
    private readonly ILogger<EmailModel> _logger;

    public EmailModel(
        IOptionsMonitor<N24DataRelayConfiguration> config,
        ConfigWriterService writer,
        ILogger<EmailModel> logger)
    {
        _config = config;
        _writer = writer;
        _logger = logger;
    }

    // ── Bound input ──────────────────────────────────────────────────────────
    [BindProperty] public SmtpInput Smtp { get; set; } = new();
    [BindProperty] public string? TestRecipient { get; set; }

    // ── Page state ───────────────────────────────────────────────────────────
    [TempData] public string? StatusMessage { get; set; }

    public string PasswordStatus { get; private set; } = "not set";

    /// <summary>Set after OnPostTestAsync — null = not yet run, empty string = success, otherwise error text.</summary>
    public string? TestResult { get; private set; }
    public bool TestRan { get; private set; }

    // ── Input model ──────────────────────────────────────────────────────────
    public class SmtpInput
    {
        public bool Enabled { get; set; }

        [Required] public string Host { get; set; } = "";
        [Range(1, 65535)] public int Port { get; set; } = 587;

        /// <summary>"StartTls" | "Ssl" | "None"</summary>
        public string Security { get; set; } = "StartTls";

        public string Username { get; set; } = "";

        /// <summary>Leave blank to keep existing password. Set via N24DataRelay__Smtp__Password env var for production.</summary>
        public string? Password { get; set; }

        [Required, EmailAddress] public string FromAddress { get; set; } = "";
        [Required] public string FromName { get; set; } = "N24 Data Relay";
        [Range(5, 120)] public int TimeoutSeconds { get; set; } = 30;
    }

    // ── GET ──────────────────────────────────────────────────────────────────
    public void OnGet()
    {
        PopulateFromConfig();
        SetPasswordStatus();
    }

    private void PopulateFromConfig()
    {
        var s = _config.CurrentValue.Smtp;
        Smtp = new SmtpInput
        {
            Enabled        = s.Enabled,
            Host           = s.Host,
            Port           = s.Port,
            Security       = s.Security,
            Username       = s.Username,
            Password       = null,   // never pre-fill
            FromAddress    = s.FromAddress,
            FromName       = s.FromName,
            TimeoutSeconds = s.TimeoutSeconds
        };
        TestRecipient = User.Identity?.Name; // default test recipient = current admin
    }

    private void SetPasswordStatus()
    {
        var envPwd = Environment.GetEnvironmentVariable("N24DataRelay__Smtp__Password")
                  ?? Environment.GetEnvironmentVariable("N24DATARELAY__SMTP__PASSWORD");

        if (!string.IsNullOrEmpty(envPwd))
            PasswordStatus = "set via environment variable";
        else if (!string.IsNullOrEmpty(_config.CurrentValue.Smtp.Password))
            PasswordStatus = "set in config file";
        else
            PasswordStatus = "not set";
    }

    // ── POST: save ───────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostSaveAsync()
    {
        // Clear test-only fields from model state
        ModelState.Remove(nameof(TestRecipient));

        if (!ModelState.IsValid)
        {
            SetPasswordStatus();
            return Page();
        }

        var cfg = ConfigWriterService.Clone(_config.CurrentValue);

        cfg.Smtp.Enabled        = Smtp.Enabled;
        cfg.Smtp.Host           = Smtp.Host.Trim();
        cfg.Smtp.Port           = Smtp.Port;
        cfg.Smtp.Security       = Smtp.Security;
        cfg.Smtp.Username       = Smtp.Username.Trim();
        cfg.Smtp.FromAddress    = Smtp.FromAddress.Trim();
        cfg.Smtp.FromName       = Smtp.FromName.Trim();
        cfg.Smtp.TimeoutSeconds = Smtp.TimeoutSeconds;

        // Preserve existing password if the field was left blank
        if (!string.IsNullOrEmpty(Smtp.Password))
            cfg.Smtp.Password = Smtp.Password;

        await _writer.WriteAsync(cfg);

        StatusMessage = "SMTP settings saved.";
        return RedirectToPage();
    }

    // ── POST: test (send without saving) ─────────────────────────────────────
    public async Task<IActionResult> OnPostTestAsync()
    {
        ModelState.Remove(nameof(TestRecipient));
        if (!ModelState.IsValid)
        {
            SetPasswordStatus();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(TestRecipient))
        {
            ModelState.AddModelError(nameof(TestRecipient), "Enter a recipient address for the test.");
            SetPasswordStatus();
            return Page();
        }

        // Resolve password: form field → env var → saved config
        var password = !string.IsNullOrEmpty(Smtp.Password)
            ? Smtp.Password
            : Environment.GetEnvironmentVariable("N24DataRelay__Smtp__Password")
           ?? Environment.GetEnvironmentVariable("N24DATARELAY__SMTP__PASSWORD")
           ?? _config.CurrentValue.Smtp.Password;

        var error = await SmtpEmailSender.TrySendAsync(
            host:           Smtp.Host.Trim(),
            port:           Smtp.Port,
            security:       Smtp.Security,
            username:       Smtp.Username.Trim(),
            password:       password ?? "",
            fromAddress:    Smtp.FromAddress.Trim(),
            fromName:       Smtp.FromName.Trim(),
            timeoutSeconds: Smtp.TimeoutSeconds,
            testTo:         TestRecipient.Trim(),
            logger:         _logger);

        TestRan    = true;
        TestResult = error; // null = success
        SetPasswordStatus();
        return Page();
    }
}
