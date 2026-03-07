using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DashboardModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ITransferTracker _tracker;

    public DashboardModel(ApplicationDbContext db, ITransferTracker tracker)
    {
        _db = db;
        _tracker = tracker;
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    public int TodayCount { get; private set; }
    public long TodayBytes { get; private set; }
    public int TodayFailed { get; private set; }
    public double? AvgThroughput { get; private set; }
    public double SuccessRate { get; private set; }

    // ── Recent activity ───────────────────────────────────────────────────────

    public List<RecentTransferVm> RecentTransfers { get; private set; } = new();
    public List<RecentAuditVm> RecentAudit { get; private set; } = new();

    // ── Service health ────────────────────────────────────────────────────────

    public DateTime? LastFileDetected { get; private set; }
    public DateTime? LastSuccess { get; private set; }
    public int QueueDepth { get; private set; }

    // ── View models ───────────────────────────────────────────────────────────

    public class RecentTransferVm
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? FileSize { get; set; }
        public int RetryCount { get; set; }
        public double? ThroughputBytesPerSec { get; set; }
        public bool Verified { get; set; }
        public string? TransferMethod { get; set; }
        public string? RemoteHost { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RecentAuditVm
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? Actor { get; set; }
        public string? Subject { get; set; }
        public string? IpAddress { get; set; }
    }

    public async Task OnGetAsync()
    {
        var todayUtc = DateTime.UtcNow.Date;

        // Stats for today
        var todayRecords = await _db.TransferRecords
            .Where(r => r.QueuedAt >= todayUtc)
            .ToListAsync();

        TodayCount  = todayRecords.Count;
        TodayBytes  = todayRecords.Sum(r => r.FileSize ?? 0);
        TodayFailed = todayRecords.Count(r => r.Status == nameof(TransferStatus.Failed));

        var completed = todayRecords.Where(r => r.Status == nameof(TransferStatus.Completed)).ToList();
        if (completed.Count > 0)
        {
            var throughputs = completed
                .Where(r => r.ThroughputBytesPerSec.HasValue && r.ThroughputBytesPerSec > 0)
                .Select(r => r.ThroughputBytesPerSec!.Value)
                .ToList();
            if (throughputs.Count > 0)
                AvgThroughput = throughputs.Average();
        }

        SuccessRate = TodayCount > 0
            ? Math.Round((double)(TodayCount - TodayFailed) / TodayCount * 100, 1)
            : 100.0;

        // Recent transfers
        var recentRows = await _db.TransferRecords
            .OrderByDescending(r => r.QueuedAt)
            .Take(15)
            .ToListAsync();

        RecentTransfers = recentRows.Select(r => new RecentTransferVm
        {
            Id                  = r.Id,
            FileName            = r.FileName,
            UploadedBy          = r.UploadedBy,
            Status              = r.Status,
            QueuedAt            = r.QueuedAt,
            CompletedAt         = r.CompletedAt,
            FileSize            = r.FileSize,
            RetryCount          = r.RetryCount,
            ThroughputBytesPerSec = r.ThroughputBytesPerSec,
            Verified            = r.Verified,
            TransferMethod      = r.TransferMethod,
            RemoteHost          = r.RemoteHost,
            ErrorMessage        = r.ErrorMessage
        }).ToList();

        // Audit feed
        var auditRows = await _db.AuditEvents
            .OrderByDescending(a => a.Timestamp)
            .Take(15)
            .ToListAsync();

        RecentAudit = auditRows.Select(a => new RecentAuditVm
        {
            Timestamp = a.Timestamp,
            EventType = a.EventType,
            Actor     = a.Actor,
            Subject   = a.Subject,
            IpAddress = a.IpAddress
        }).ToList();

        // Service health from in-memory tracker
        var all = _tracker.GetRecent(500);
        LastSuccess      = all.Where(r => r.Status == TransferStatus.Completed).Select(r => r.CompletedAt).FirstOrDefault();
        LastFileDetected = all.Select(r => r.QueuedAt).FirstOrDefault();
        QueueDepth       = all.Count(r => r.Status == TransferStatus.Queued || r.Status == TransferStatus.Transferring);
    }
}
