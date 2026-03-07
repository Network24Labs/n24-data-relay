using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// SQLite-backed transfer tracker. Keeps a hot in-memory cache for fast SignalR access
/// and persists all changes to SQLite via a serialized background write queue.
/// Survives restarts: recent records are reloaded from the database on startup.
/// </summary>
public sealed class SqliteTransferTracker : ITransferTracker, IHostedService
{
    private const int MaxCachedRecords = 500;

    private readonly ConcurrentDictionary<string, TransferStatusRecord> _records = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqliteTransferTracker> _logger;

    // Single-reader channel serializes SQLite writes to avoid "database is locked" errors.
    private readonly Channel<TransferStatusRecord> _writeQueue =
        Channel.CreateUnbounded<TransferStatusRecord>(new() { SingleReader = true });

    private CancellationTokenSource? _cts;

    public event EventHandler<TransferStatusRecord>? StatusChanged;

    public SqliteTransferTracker(IServiceScopeFactory scopeFactory, ILogger<SqliteTransferTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.TransferRecords
                .OrderByDescending(r => r.QueuedAt)
                .Take(MaxCachedRecords)
                .ToListAsync(cancellationToken);

            foreach (var row in rows.OrderBy(r => r.QueuedAt))
            {
                var record = row.ToStatusRecord();
                _records[record.Id] = record;
                _order.Enqueue(record.Id);
            }
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ProcessWriteQueueAsync(_cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _writeQueue.Writer.TryComplete();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

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
        _writeQueue.Writer.TryWrite(record);
        StatusChanged?.Invoke(this, record);
        return record;
    }

    public void UpdateStatus(string id, TransferStatus status, string? errorMessage = null, string? destinationPath = null)
    {
        if (!_records.TryGetValue(id, out var record)) return;

        record.Status = status;
        if (errorMessage != null) record.ErrorMessage = errorMessage;
        if (destinationPath != null) record.DestinationPath = destinationPath;

        if (status == TransferStatus.Transferring)
            record.TransferStartedAt = DateTime.UtcNow;
        else if (status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Archived)
            record.CompletedAt = DateTime.UtcNow;

        _writeQueue.Writer.TryWrite(record);
        StatusChanged?.Invoke(this, record);
    }

    public IReadOnlyList<TransferStatusRecord> GetRecent(int max = 50)
        => _records.Values.OrderByDescending(r => r.QueuedAt).Take(max).ToList();

    public TransferStatusRecord? GetById(string id)
        => _records.TryGetValue(id, out var r) ? r : null;

    public TransferStatusRecord? FindBySourcePath(string sourcePath)
        => _records.Values.FirstOrDefault(r =>
            string.Equals(r.SourcePath, sourcePath, StringComparison.Ordinal));

    private void Trim()
    {
        while (_order.Count > MaxCachedRecords && _order.TryDequeue(out var oldId))
            _records.TryRemove(oldId, out _);
    }

    private async Task ProcessWriteQueueAsync(CancellationToken ct)
    {
        await foreach (var snapshot in _writeQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var existing = await db.TransferRecords.FindAsync(new object[] { snapshot.Id }, ct);
                if (existing == null)
                {
                    db.TransferRecords.Add(TransferRecord.FromStatusRecord(snapshot));
                }
                else
                {
                    existing.Status = snapshot.Status.ToString();
                    existing.ErrorMessage = snapshot.ErrorMessage;
                    existing.DestinationPath = snapshot.DestinationPath;
                    existing.TransferStartedAt = snapshot.TransferStartedAt;
                    existing.CompletedAt = snapshot.CompletedAt;
                    existing.FileSize = snapshot.FileSize;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist transfer record {Id}", snapshot.Id);
            }
        }
    }
}
