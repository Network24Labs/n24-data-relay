using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.Watcher;

/// <summary>Background worker: file watcher + queue + transfer with retry and optional archive.</summary>
public sealed class TransferWorker : BackgroundService
{
    private readonly ILogger<TransferWorker> _logger;
    private readonly N24DataRelayConfiguration _config;
    private readonly IFileWatcher _fileWatcher;
    private readonly IFileQueue _fileQueue;
    private readonly IFileTransferServiceFactory _transferFactory;
    private readonly ITransferTracker _tracker;
    private Timer? _processTimer;
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
    private CancellationToken _stoppingToken;

    // maps file path → tracker record ID so we can update the same record on retry
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _fileToTrackerId = new();

    public TransferWorker(
        ILogger<TransferWorker> logger,
        IOptions<N24DataRelayConfiguration> options,
        IFileWatcher fileWatcher,
        IFileQueue fileQueue,
        IFileTransferServiceFactory transferFactory,
        ITransferTracker tracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _fileQueue = fileQueue ?? throw new ArgumentNullException(nameof(fileQueue));
        _transferFactory = transferFactory ?? throw new ArgumentNullException(nameof(transferFactory));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        if (!_config.Service.Enabled)
        {
            _logger.LogInformation("Watcher is disabled (Service.Enabled = false).");
            return;
        }

        var watchDir = _config.Service.WatchDirectory;
        if (string.IsNullOrEmpty(watchDir))
        {
            _logger.LogWarning("WatchDirectory is not configured.");
            return;
        }

        if (!Directory.Exists(watchDir))
        {
            try
            {
                Directory.CreateDirectory(watchDir);
                _logger.LogInformation("Created watch directory: {Path}", watchDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot create watch directory: {Path}", watchDir);
                return;
            }
        }

        _fileWatcher.FileDetected += OnFileDetected;
        _fileWatcher.FileChanged += OnFileChanged;
        _fileWatcher.StartWatching(watchDir, _config.Service.IncludeSubdirectories);

        _processTimer = new Timer(
            ProcessPendingFiles,
            null,
            TimeSpan.FromSeconds(_config.Service.ProcessingIntervalSeconds),
            TimeSpan.FromSeconds(_config.Service.ProcessingIntervalSeconds));

        _logger.LogInformation("Transfer worker started. WatchDirectory: {Path}, TransferMethod: {Method}", watchDir, _config.Service.TransferMethod);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transfer worker stopping...");
        _processTimer?.Dispose();
        _fileWatcher.FileDetected -= OnFileDetected;
        _fileWatcher.FileChanged -= OnFileChanged;
        _fileWatcher.StopWatching();
        _fileWatcher.Dispose();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnFileDetected(object? sender, FileSystemEventArgs e)
    {
        try
        {
            var fullPath = e.FullPath;
            if (!fullPath.StartsWith(_config.Service.WatchDirectory, StringComparison.Ordinal))
            {
                _logger.LogDebug("Ignoring file outside watch directory: {Path}", fullPath);
                return;
            }
            var statusDir = Path.Combine(_config.Service.WatchDirectory, ".status");
            if (fullPath.StartsWith(statusDir, StringComparison.Ordinal))
            {
                _logger.LogDebug("Ignoring status file: {Path}", fullPath);
                return;
            }
            if (_fileQueue.Count >= _config.Service.MaxQueueSize)
            {
                _logger.LogWarning("Queue full ({Max}). Ignoring: {Path}", _config.Service.MaxQueueSize, fullPath);
                return;
            }
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("File detected: {Path}", fullPath);
                _fileQueue.TryEnqueue(fullPath);

                // Reuse the tracker record created by FileUploadService if it exists,
                // otherwise create a new one (e.g. for files dropped directly into WatchDirectory)
                var fileInfo = new FileInfo(fullPath);
                var existing = _tracker.FindBySourcePath(fullPath);
                var record = existing ?? _tracker.Enqueue(
                    fileName: fileInfo.Name,
                    sourcePath: fullPath,
                    uploadedBy: DeriveUploadedBy(fullPath),
                    fileSize: fileInfo.Exists ? fileInfo.Length : null);
                _fileToTrackerId[fullPath] = record.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file detected: {Path}", e.FullPath);
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        if (File.Exists(e.FullPath))
            _fileQueue.UpdateFileActivity(e.FullPath);
    }

    private static string DeriveUploadedBy(string filePath)
    {
        // Files are saved as WatchDir/<username>/<file>; derive from parent folder name
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath));
        return string.IsNullOrEmpty(parent) ? "unknown" : parent;
    }


    private void ProcessPendingFiles(object? state)
    {
        if (!_processingSemaphore.Wait(0))
            return;
        _ = ProcessPendingFilesAsync()
            .ContinueWith(_ => _processingSemaphore.Release(), TaskContinuationOptions.ExecuteSynchronously);
    }

    private async Task ProcessPendingFilesAsync()
    {
        try
        {
            var maxPerCycle = 10;
            var processed = 0;

            while (_fileQueue.TryPeek(out var filePath) && processed < maxPerCycle && !_stoppingToken.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    _fileQueue.Remove(filePath ?? string.Empty);
                    processed++;
                    continue;
                }

                if (!File.Exists(filePath))
                {
                    _fileQueue.Remove(filePath);
                    processed++;
                    continue;
                }

                if (!_fileQueue.IsFileStable(filePath, _config.Service.FileStabilitySeconds))
                    break;

                _fileToTrackerId.TryGetValue(filePath, out var trackerId);

                // Mark as Transferring (StatusChanged event will fire; WebApp listener pushes to SignalR)
                if (trackerId != null)
                    _tracker.UpdateStatus(trackerId, TransferStatus.Transferring);

                var retries = _config.Service.RetryAttempts;
                var delaySec = _config.Service.RetryDelaySeconds;
                var backoff = _config.Service.RetryBackoffMultiplier;
                TransferResult? result = null;

                for (var attempt = 0; attempt <= retries; attempt++)
                {
                    try
                    {
                        var svc = _transferFactory.CreateTransferService();
                        result = await svc.TransferFileAsync(filePath, null, _stoppingToken).ConfigureAwait(false);
                        if (result.Success)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Transfer attempt {Attempt} failed for {Path}", attempt + 1, filePath);
                        if (attempt < retries)
                            await Task.Delay(TimeSpan.FromSeconds(delaySec * Math.Pow(backoff, attempt)), _stoppingToken).ConfigureAwait(false);
                        else
                            result = new TransferResult { Success = false, ErrorMessage = ex.Message, FileName = Path.GetFileName(filePath), SourcePath = filePath };
                    }
                }

                if (result != null && result.Success)
                {
                    _logger.LogInformation("Transferred: {FileName}", result.FileName);
                    if (trackerId != null)
                        _tracker.UpdateStatus(trackerId, TransferStatus.Completed, destinationPath: result.DestinationPath);

                    if (_config.Service.ArchiveAfterTransfer && !string.IsNullOrEmpty(_config.Service.ArchiveDirectory))
                    {
                        try
                        {
                            Directory.CreateDirectory(_config.Service.ArchiveDirectory);
                            var archivePath = Path.Combine(_config.Service.ArchiveDirectory, Path.GetFileName(filePath));
                            File.Move(filePath, archivePath, overwrite: true);
                            if (trackerId != null)
                                _tracker.UpdateStatus(trackerId, TransferStatus.Archived);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not archive {Path}", filePath);
                        }
                    }
                    else if (_config.Service.DeleteAfterTransfer)
                    {
                        try
                        {
                            File.Delete(filePath);
                            // Remove the per-user subdirectory if it is now empty.
                            var dir = Path.GetDirectoryName(filePath);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                                && !Directory.EnumerateFileSystemEntries(dir).Any())
                                Directory.Delete(dir);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete after transfer: {Path}", filePath); }
                    }
                }
                else if (result != null)
                {
                    _logger.LogError("Transfer failed: {FileName} - {Error}", result.FileName, result.ErrorMessage);
                    if (trackerId != null)
                        _tracker.UpdateStatus(trackerId, TransferStatus.Failed, errorMessage: result.ErrorMessage);
                }

                _fileToTrackerId.TryRemove(filePath, out _);

                _fileQueue.Remove(filePath);
                processed++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessPendingFilesAsync");
        }
    }
}
