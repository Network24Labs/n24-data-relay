using N24DataRelay.Core.Models;

namespace N24DataRelay.Core.Interfaces;

/// <summary>
/// Tracks recent transfer status records and notifies subscribers of changes.
/// Implemented in WebApp as a singleton; the Watcher references the interface via Core.
/// </summary>
public interface ITransferTracker
{
    TransferStatusRecord Enqueue(string fileName, string sourcePath, string uploadedBy, long? fileSize = null);
    void UpdateStatus(string id, TransferStatus status, string? errorMessage = null, string? destinationPath = null);
    IReadOnlyList<TransferStatusRecord> GetRecent(int max = 50);
    TransferStatusRecord? GetById(string id);
    TransferStatusRecord? FindBySourcePath(string sourcePath);

    /// <summary>Raised whenever a record is added or its status changes.</summary>
    event EventHandler<TransferStatusRecord> StatusChanged;
}
