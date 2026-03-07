namespace N24DataRelay.Core.Models;

/// <summary>Result of a file transfer operation.</summary>
public class TransferResult
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string? DestinationPath { get; set; }
    public long FileSize { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
    public int RetryCount { get; set; }
    public string TransferMethod { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string? Checksum { get; set; }
    public string? RemoteHost { get; set; }
}
