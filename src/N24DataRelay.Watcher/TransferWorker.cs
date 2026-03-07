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
    private readonly IOptionsMonitor<N24DataRelayConfiguration> _options;
    private N24DataRelayConfiguration Config => _options.CurrentValue;
    private readonly IFileWatcher _fileWatcher;
    private readonly IFileQueue _fileQueue;
    private readonly IFileTransferServiceFactory _transferFactory;
    private readonly ITransferTracker _tracker;
    private readonly IAuditLogger _audit;
    private readonly N24DataRelay.Core.Interfaces.IEmailSender _emailSender;
    private Timer? _processTimer;
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
    private CancellationToken _stoppingToken;

    // maps file path → tracker record ID so we can update the same record on retry
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _fileToTrackerId = new();

    public TransferWorker(
        ILogger<TransferWorker> logger,
        IOptionsMonitor<N24DataRelayConfiguration> options,
        IFileWatcher fileWatcher,
        IFileQueue fileQueue,
        IFileTransferServiceFactory transferFactory,
        ITransferTracker tracker,
        IAuditLogger audit,
        N24DataRelay.Core.Interfaces.IEmailSender emailSender)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _fileQueue = fileQueue ?? throw new ArgumentNullException(nameof(fileQueue));
        _transferFactory = transferFactory ?? throw new ArgumentNullException(nameof(transferFactory));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        if (!Config.Service.Enabled)
        {
            _logger.LogInformation("Watcher is disabled (Service.Enabled = false).");
            return;
        }

        var watchDir = Config.Service.WatchDirectory;
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
        _fileWatcher.StartWatching(watchDir, Config.Service.IncludeSubdirectories, Config.Service.FileFilter);

        _processTimer = new Timer(
            ProcessPendingFiles,
            null,
            TimeSpan.FromSeconds(Config.Service.ProcessingIntervalSeconds),
            TimeSpan.FromSeconds(Config.Service.ProcessingIntervalSeconds));

        _logger.LogInformation("Transfer worker started. WatchDirectory: {Path}, TransferMethod: {Method}, FileFilter: {Filter}",
            watchDir, Config.Service.TransferMethod, Config.Service.FileFilter);
        _audit.Log(AuditEventTypes.ServiceStarted, "system", details: new { watchDir, transferMethod = Config.Service.TransferMethod });

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transfer worker stopping...");
        _audit.Log(AuditEventTypes.ServiceStopped, "system");
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
            if (!fullPath.StartsWith(Config.Service.WatchDirectory, StringComparison.Ordinal))
            {
                _logger.LogDebug("Ignoring file outside watch directory: {Path}", fullPath);
                return;
            }
            var statusDir = Path.Combine(Config.Service.WatchDirectory, ".status");
            if (fullPath.StartsWith(statusDir, StringComparison.Ordinal))
            {
                _logger.LogDebug("Ignoring status file: {Path}", fullPath);
                return;
            }
            if (_fileQueue.Count >= Config.Service.MaxQueueSize)
            {
                _logger.LogWarning("Queue full ({Max}). Ignoring: {Path}", Config.Service.MaxQueueSize, fullPath);
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

                if (!_fileQueue.IsFileStable(filePath, Config.Service.FileStabilitySeconds))
                    break;

                _fileToTrackerId.TryGetValue(filePath, out var trackerId);

                // Mark as Transferring (StatusChanged event will fire; WebApp listener pushes to SignalR)
                if (trackerId != null)
                    _tracker.UpdateStatus(trackerId, TransferStatus.Transferring);

                var retries = Config.Service.RetryAttempts;
                var delaySec = Config.Service.RetryDelaySeconds;
                var backoff = Config.Service.RetryBackoffMultiplier;
                TransferResult? result = null;

                for (var attempt = 0; attempt <= retries; attempt++)
                {
                    try
                    {
                        var svc = _transferFactory.CreateTransferService();
                        result = await svc.TransferFileAsync(filePath, null, _stoppingToken).ConfigureAwait(false);
                        result.RetryCount = attempt;
                        if (result.Success)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Transfer attempt {Attempt} failed for {Path}", attempt + 1, filePath);
                        if (attempt < retries)
                            await Task.Delay(TimeSpan.FromSeconds(delaySec * Math.Pow(backoff, attempt)), _stoppingToken).ConfigureAwait(false);
                        else
                            result = new TransferResult { Success = false, ErrorMessage = ex.Message, ErrorDetails = ex.ToString(), RetryCount = attempt, FileName = Path.GetFileName(filePath), SourcePath = filePath };
                    }
                }

                if (result != null && result.Success)
                {
                    _logger.LogInformation("Transferred: {FileName}", result.FileName);
                    if (trackerId != null)
                        _tracker.UpdateStatus(trackerId, TransferStatus.Completed, destinationPath: result.DestinationPath, result: result);

                    if (Config.Service.ArchiveAfterTransfer && !string.IsNullOrEmpty(Config.Service.ArchiveDirectory))
                    {
                        try
                        {
                            Directory.CreateDirectory(Config.Service.ArchiveDirectory);
                            var archivePath = Path.Combine(Config.Service.ArchiveDirectory, Path.GetFileName(filePath));
                            File.Move(filePath, archivePath, overwrite: true);
                            if (trackerId != null)
                                _tracker.UpdateStatus(trackerId, TransferStatus.Archived);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not archive {Path}", filePath);
                        }
                    }
                    else if (Config.Service.DeleteAfterTransfer)
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
                        _tracker.UpdateStatus(trackerId, TransferStatus.Failed, errorMessage: result.ErrorMessage, result: result);
                    _audit.Log(AuditEventTypes.TransferFailed, "system", subject: result.FileName,
                        details: new { result.ErrorMessage, result.RetryCount, result.TransferMethod, result.RemoteHost });

                    // Send failure notification email if a support address is configured.
                    var supportEmail = Config.Branding.SupportEmail;
                    if (!string.IsNullOrWhiteSpace(supportEmail))
                    {
                        try
                        {
                            await _emailSender.SendAsync(
                                to: supportEmail,
                                subject: $"[N24 Data Relay] Transfer failed: {result.FileName}",
                                htmlBody: $"""
                                    <h3>Transfer Failure Alert</h3>
                                    <p>A file transfer has failed after exhausting all retry attempts.</p>
                                    <table>
                                        <tr><td><strong>File:</strong></td><td>{result.FileName}</td></tr>
                                        <tr><td><strong>Source:</strong></td><td>{result.SourcePath}</td></tr>
                                        <tr><td><strong>Error:</strong></td><td>{result.ErrorMessage}</td></tr>
                                        <tr><td><strong>Retries:</strong></td><td>{result.RetryCount}</td></tr>
                                        <tr><td><strong>Method:</strong></td><td>{result.TransferMethod}</td></tr>
                                        <tr><td><strong>Remote host:</strong></td><td>{result.RemoteHost}</td></tr>
                                        <tr><td><strong>Time:</strong></td><td>{DateTime.UtcNow:u}</td></tr>
                                    </table>
                                    """).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not send failure notification email.");
                        }
                    }
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
