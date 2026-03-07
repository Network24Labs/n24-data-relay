namespace N24DataRelay.Core.Models;

/// <summary>Result of a file upload operation.</summary>
public class UploadResult
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadTime { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool RequiresTransfer { get; set; }
    public string? Notes { get; set; }
    /// <summary>ID of the ITransferTracker record created at upload time (set when RequiresTransfer = true).</summary>
    public string? TrackerId { get; set; }
}
