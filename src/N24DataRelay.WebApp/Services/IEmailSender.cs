namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Abstraction for sending transactional emails (password reset, etc.).
/// The default implementation logs the message; replace with an SMTP
/// implementation when email delivery is configured.
/// </summary>
public interface IEmailSender
{
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">HTML body. Plain-text links must be embedded so the
    ///   logging implementation can surface them to an admin.</param>
    Task SendAsync(string to, string subject, string htmlBody);
}
