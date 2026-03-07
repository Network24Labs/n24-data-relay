using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Production email sender using MailKit/SMTP.
/// Activated automatically when <c>N24DataRelay:Smtp:Enabled = true</c> and a host is set.
///
/// Password resolution order:
///   1. <c>N24DataRelay__Smtp__Password</c> environment variable (recommended for production)
///   2. <c>N24DataRelay:Smtp:Password</c> config value (acceptable for dev / internal use)
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptionsMonitor<N24DataRelayConfiguration> config,
        ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var cfg = _config.CurrentValue.Smtp;

        var password = Environment.GetEnvironmentVariable("N24DataRelay__Smtp__Password")
                    ?? Environment.GetEnvironmentVariable("N24DATARELAY__SMTP__PASSWORD")
                    ?? cfg.Password;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var socketOpts = cfg.Security switch
        {
            "Ssl"      => SecureSocketOptions.SslOnConnect,
            "None"     => SecureSocketOptions.None,
            _          => SecureSocketOptions.StartTls   // "StartTls" default
        };

        using var client = new SmtpClient();
        client.Timeout = cfg.TimeoutSeconds * 1000;

        _logger.LogDebug("SMTP: connecting to {Host}:{Port} ({Security})", cfg.Host, cfg.Port, cfg.Security);
        await client.ConnectAsync(cfg.Host, cfg.Port, socketOpts);

        if (!string.IsNullOrEmpty(cfg.Username))
            await client.AuthenticateAsync(cfg.Username, password);

        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);

        _logger.LogInformation("Email sent to {To} — {Subject}", to, subject);
    }

    /// <summary>
    /// Connects and optionally sends a test message using the provided parameters
    /// (not the saved config). Used by the admin test-send form before saving.
    /// </summary>
    public static async Task<string?> TrySendAsync(
        string host, int port, string security, string username, string password,
        string fromAddress, string fromName, int timeoutSeconds,
        string testTo, ILogger logger)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(testTo));
            message.Subject = "N24 Data Relay — SMTP Test";
            message.Body = new BodyBuilder
            {
                HtmlBody = """
                    <p>This is a test message from <strong>N24 Data Relay</strong>.</p>
                    <p>If you received this, your SMTP configuration is working correctly.</p>
                    """
            }.ToMessageBody();

            var socketOpts = security switch
            {
                "Ssl"  => SecureSocketOptions.SslOnConnect,
                "None" => SecureSocketOptions.None,
                _      => SecureSocketOptions.StartTls
            };

            using var client = new SmtpClient();
            client.Timeout = timeoutSeconds * 1000;

            await client.ConnectAsync(host, port, socketOpts);
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);

            return null; // success
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP test failed");
            return ex.Message;
        }
    }
}
