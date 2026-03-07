namespace N24DataRelay.Core.Models;

public enum TransferStatus
{
    Queued,
    Transferring,
    Completed,
    Failed,
    Archived
}

public class TransferStatusRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FileName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string UploadedBy { get; init; } = string.Empty;
    public TransferStatus Status { get; set; } = TransferStatus.Queued;
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;
    public DateTime? TransferStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public long? FileSize { get; set; }
    public string? DestinationPath { get; set; }
    /// <summary>Duration in milliseconds; computed server-side so the client doesn't need to.</summary>
    public long? DurationMs => CompletedAt.HasValue && TransferStartedAt.HasValue
        ? (long)(CompletedAt.Value - TransferStartedAt.Value).TotalMilliseconds
        : null;

    // Phase 3 observability fields
    public int RetryCount { get; set; }
    public double? ThroughputBytesPerSec { get; set; }
    public bool Verified { get; set; }
    public string? ErrorDetails { get; set; }
    public string? TransferMethod { get; set; }
    public string? RemoteHost { get; set; }
}
