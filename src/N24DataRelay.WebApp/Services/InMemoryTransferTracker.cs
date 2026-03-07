using System.Collections.Concurrent;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// Thread-safe, in-memory store of recent transfer records. Capped at MaxRecords entries
/// (oldest removed first). Raises StatusChanged for SignalR to push to clients.
/// </summary>
public sealed class InMemoryTransferTracker : ITransferTracker
{
    private const int MaxRecords = 200;
    private readonly ConcurrentDictionary<string, TransferStatusRecord> _records = new();
    private readonly ConcurrentQueue<string> _order = new();

    public event EventHandler<TransferStatusRecord>? StatusChanged;

    public TransferStatusRecord Enqueue(string fileName, string sourcePath, string uploadedBy, long? fileSize = null)
    {
        var record = new TransferStatusRecord
        {
            FileName = fileName,
            SourcePath = sourcePath,
            UploadedBy = uploadedBy,
            FileSize = fileSize,
            Status = TransferStatus.Queued
        };

        _records[record.Id] = record;
        _order.Enqueue(record.Id);
        Trim();
        StatusChanged?.Invoke(this, record);
        return record;
    }

    public void UpdateStatus(string id, TransferStatus status, string? errorMessage = null,
        string? destinationPath = null, TransferResult? result = null)
    {
        if (!_records.TryGetValue(id, out var record)) return;

        record.Status = status;
        if (errorMessage != null) record.ErrorMessage = errorMessage;
        if (destinationPath != null) record.DestinationPath = destinationPath;

        if (status == TransferStatus.Transferring)
            record.TransferStartedAt = DateTime.UtcNow;
        else if (status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Archived)
            record.CompletedAt = DateTime.UtcNow;

        if (result != null)
        {
            record.RetryCount = result.RetryCount;
            record.Verified = result.Verified;
            record.ErrorDetails = result.ErrorDetails;
            record.TransferMethod = result.TransferMethod;
            record.RemoteHost = result.RemoteHost;
            if (result.FileSize > 0 && result.Duration.HasValue && result.Duration.Value.TotalMilliseconds > 0)
                record.ThroughputBytesPerSec = result.FileSize / result.Duration.Value.TotalMilliseconds * 1000.0;
        }

        StatusChanged?.Invoke(this, record);
    }

    public IReadOnlyList<TransferStatusRecord> GetRecent(int max = 50)
    {
        return _records.Values
            .OrderByDescending(r => r.QueuedAt)
            .Take(max)
            .ToList();
    }

    public TransferStatusRecord? GetById(string id) =>
        _records.TryGetValue(id, out var r) ? r : null;

    public TransferStatusRecord? FindBySourcePath(string sourcePath) =>
        _records.Values.FirstOrDefault(r => string.Equals(r.SourcePath, sourcePath, StringComparison.Ordinal));

    private void Trim()
    {
        while (_order.Count > MaxRecords && _order.TryDequeue(out var oldId))
            _records.TryRemove(oldId, out _);
    }
}
