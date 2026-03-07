namespace N24DataRelay.Core.Interfaces;

/// <summary>
/// Abstraction for sending transactional emails.
/// Implementations are registered in the WebApp; the Watcher references this interface
/// to send failure notifications without a circular dependency.
/// </summary>
public interface IEmailSender
{
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">HTML body. Plain-text links must be embedded so the
    ///   logging implementation can surface them to an admin.</param>
    Task SendAsync(string to, string subject, string htmlBody);
}
