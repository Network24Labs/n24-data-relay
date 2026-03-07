using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Controllers;

/// <summary>
/// Polling API for external monitoring tools (LogScale, Grafana, etc.).
/// All endpoints except /health require a Bearer API key set in configuration.
/// </summary>
[ApiController]
[Route("api/v1")]
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
        _db = db;
        _config = config;
        _tracker = tracker;
    }

    // ── GET /api/v1/health — unauthenticated heartbeat ───────────────────────

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult GetHealth()
    {
        var recent = _tracker.GetRecent(1).FirstOrDefault();
        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.UtcNow,
            lastTransferAt = recent?.CompletedAt,
            lastTransferFile = recent?.FileName,
            queueDepth = _tracker.GetRecent(500).Count(r =>
                r.Status == TransferStatus.Queued || r.Status == TransferStatus.Transferring)
        });
    }

    // ── GET /api/v1/transfers — paginated transfer records ───────────────────

    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransfers(
        [FromQuery] DateTime? since,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Invalid or missing API key." });

        limit = Math.Clamp(limit, 1, 1000);
        var cutoff = since ?? DateTime.UtcNow.AddDays(-1);

        var rows = await _db.TransferRecords
            .Where(r => r.QueuedAt >= cutoff)
            .OrderByDescending(r => r.QueuedAt)
            .Take(limit)
            .Select(r => new
            {
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
                r.RemoteHost
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ── GET /api/v1/audit — paginated audit events ───────────────────────────

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit(
        [FromQuery] DateTime? since,
        [FromQuery] string? eventType,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Invalid or missing API key." });

        limit = Math.Clamp(limit, 1, 1000);
        var cutoff = since ?? DateTime.UtcNow.AddDays(-1);

        var query = _db.AuditEvents
            .Where(a => a.Timestamp >= cutoff);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(a => a.EventType == eventType);

        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.EventType,
                a.Actor,
                a.Subject,
                a.Details,
                a.IpAddress
            })
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
