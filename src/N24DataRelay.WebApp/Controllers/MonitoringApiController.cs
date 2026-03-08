using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Controllers;

/// <summary>
/// Monitoring API — polling endpoints for external tools such as LogScale, Grafana, or Splunk.
/// </summary>
/// <remarks>
/// All endpoints except <c>GET /api/v1/health</c> require a Bearer API key in the
/// <c>Authorization</c> header. Configure the key in <strong>Admin → Settings → Web Portal →
/// Monitoring API Key</strong>, or via the <c>N24DataRelay__WebPortal__Authentication__ApiKey</c>
/// environment variable.
/// </remarks>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Tags("Monitoring")]
public class MonitoringApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _config;
    private readonly ITransferTracker _tracker;

    public MonitoringApiController(
        ApplicationDbContext db,
        IOptionsMonitor<N24DataRelayConfiguration> config,
        ITransferTracker tracker)
    {
        _db      = db;
        _config  = config;
        _tracker = tracker;
    }

    // ── GET /api/v1/health ───────────────────────────────────────────────────

    /// <summary>Service heartbeat — no authentication required.</summary>
    /// <remarks>
    /// Returns a lightweight status object confirming the service is running,
    /// along with the current queue depth and the time of the last completed transfer.
    /// Suitable for use as a liveness probe or uptime monitor.
    /// </remarks>
    /// <response code="200">Service is running.</response>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        var recent = _tracker.GetRecent(1).FirstOrDefault();
        return Ok(new HealthResponse(
            Status:           "ok",
            Timestamp:        DateTime.UtcNow,
            LastTransferAt:   recent?.CompletedAt,
            LastTransferFile: recent?.FileName,
            QueueDepth:       _tracker.GetRecent(500).Count(r =>
                                  r.Status == TransferStatus.Queued ||
                                  r.Status == TransferStatus.Transferring)));
    }

    // ── GET /api/v1/transfers ────────────────────────────────────────────────

    /// <summary>Returns paginated transfer records with full observability metrics.</summary>
    /// <remarks>
    /// Records are ordered newest-first. Defaults to the last 24 hours when <paramref name="since"/>
    /// is omitted. Use the <c>since</c> parameter with a stored cursor to poll incrementally.
    ///
    /// **Authentication:** Bearer API key in the <c>Authorization</c> header.
    /// </remarks>
    /// <param name="since">
    /// ISO 8601 UTC timestamp. Only records queued at or after this time are returned.
    /// Defaults to 24 hours ago when omitted.
    /// </param>
    /// <param name="limit">Maximum number of records to return (1–1000, default 500).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Array of transfer records (may be empty).</response>
    /// <response code="401">Missing, invalid, or unconfigured API key.</response>
    [HttpGet("transfers")]
    [ProducesResponseType<IEnumerable<TransferRecordDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTransfers(
        [FromQuery] DateTime? since,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (!IsAuthorised()) return Unauthorized(new ErrorResponse("Invalid or missing API key."));

        limit = Math.Clamp(limit, 1, 1000);
        var cutoff = since ?? DateTime.UtcNow.AddDays(-1);

        var rows = await _db.TransferRecords
            .Where(r => r.QueuedAt >= cutoff)
            .OrderByDescending(r => r.QueuedAt)
            .Take(limit)
            .Select(r => new TransferRecordDto(
                r.Id,
                r.FileName,
                r.UploadedBy,
                r.Status,
                r.QueuedAt,
                r.TransferStartedAt,
                r.CompletedAt,
                r.FileSize,
                r.DestinationPath,
                r.RetryCount,
                r.ThroughputBytesPerSec,
                r.Verified,
                r.ErrorMessage,
                r.ErrorDetails,
                r.TransferMethod,
                r.RemoteHost))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ── GET /api/v1/audit ────────────────────────────────────────────────────

    /// <summary>Returns paginated audit log entries.</summary>
    /// <remarks>
    /// Records are ordered newest-first. Defaults to the last 24 hours when <paramref name="since"/>
    /// is omitted. Optionally filter by <paramref name="eventType"/> to narrow results.
    ///
    /// Common event types: <c>UserLogin</c>, <c>UserLoginFailed</c>, <c>UserLogout</c>,
    /// <c>UserRegistered</c>, <c>PasswordChanged</c>, <c>ConfigSaved</c>,
    /// <c>TransferFailed</c>, <c>ServiceStarted</c>, <c>ServiceStopped</c>.
    ///
    /// **Authentication:** Bearer API key in the <c>Authorization</c> header.
    /// </remarks>
    /// <param name="since">
    /// ISO 8601 UTC timestamp. Only events at or after this time are returned.
    /// Defaults to 24 hours ago when omitted.
    /// </param>
    /// <param name="eventType">
    /// Optional filter — return only events matching this exact event type string.
    /// </param>
    /// <param name="limit">Maximum number of records to return (1–1000, default 500).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Array of audit events (may be empty).</response>
    /// <response code="401">Missing, invalid, or unconfigured API key.</response>
    [HttpGet("audit")]
    [ProducesResponseType<IEnumerable<AuditEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAudit(
        [FromQuery] DateTime? since,
        [FromQuery] string? eventType,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (!IsAuthorised()) return Unauthorized(new ErrorResponse("Invalid or missing API key."));

        limit = Math.Clamp(limit, 1, 1000);
        var cutoff = since ?? DateTime.UtcNow.AddDays(-1);

        var query = _db.AuditEvents.Where(a => a.Timestamp >= cutoff);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(a => a.EventType == eventType);

        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditEventDto(
                a.Id,
                a.Timestamp,
                a.EventType,
                a.Actor,
                a.Subject,
                a.Details,
                a.IpAddress))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsAuthorised()
    {
        var configured = _config.CurrentValue.WebPortal.Authentication.ApiKey;
        if (string.IsNullOrWhiteSpace(configured))
            return false;

        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return false;

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var provided = value["Bearer ".Length..].Trim();
        return string.Equals(provided, configured, StringComparison.Ordinal);
    }
}
