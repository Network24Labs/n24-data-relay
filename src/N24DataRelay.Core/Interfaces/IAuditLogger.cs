namespace N24DataRelay.Core.Interfaces;

/// <summary>
/// Persists security and operational audit events.
/// Fire-and-forget: implementations must never block the caller.
/// </summary>
public interface IAuditLogger
{
    /// <param name="eventType">Well-known string constant from <c>AuditEventTypes</c>.</param>
    /// <param name="actor">Email address or "system".</param>
    /// <param name="subject">Target email, filename, config section, etc.</param>
    /// <param name="details">Optional object serialised to JSON for extra context.</param>
    /// <param name="ipAddress">Remote IP from the HTTP request, if available.</param>
    void Log(string eventType, string? actor, string? subject = null,
             object? details = null, string? ipAddress = null);
}
