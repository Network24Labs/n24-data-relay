using System.Text.RegularExpressions;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Stub email sender used until SMTP is configured.
/// Logs the full message at Warning level so an administrator can retrieve
/// reset links from the application log (journalctl -u n24-data-relay).
///
/// Replace this registration with an SmtpEmailSender once the SMTP
/// settings task is complete.
/// </summary>
public sealed partial class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        // Extract href links and redact any token query parameter before logging,
        // so single-use reset tokens are never persisted in the log stream.
        var links = HrefPattern().Matches(htmlBody)
            .Select(m => TokenPattern().Replace(m.Groups[1].Value, "token=[REDACTED]"))
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList();

        _logger.LogWarning(
            "[EMAIL NOT SENT — SMTP not configured] To: {To} | Subject: {Subject} | Links: {Links}",
            to, subject, links.Count > 0 ? string.Join(" , ", links) : "(none)");

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"href=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();

    [GeneratedRegex(@"token=[^&""]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();
}
