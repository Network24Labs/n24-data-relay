using System.IO;
using Microsoft.Extensions.Logging;
using N24DataRelay.Core.Interfaces;

namespace N24DataRelay.Watcher;

/// <summary>Monitors file system for new/changed files.</summary>
public sealed class FileWatcher : IFileWatcher
{
    private readonly ILogger<FileWatcher> _logger;
    private FileSystemWatcher? _watcher;
    private readonly object _disposeLock = new();
    private bool _disposed;

    public event EventHandler<FileSystemEventArgs>? FileDetected;
    public event EventHandler<FileSystemEventArgs>? FileChanged;

    public FileWatcher(ILogger<FileWatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void StartWatching(string path, bool includeSubdirectories)
    {
        if (_watcher != null)
            throw new InvalidOperationException("Watcher is already started.");

        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = includeSubdirectories,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Changed += OnFileChanged;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation("File watcher started for path: {Path} (IncludeSubdirectories: {IncludeSub})", path, includeSubdirectories);
    }

    public void StopWatching()
    {
        lock (_disposeLock)
        {
            if (_watcher != null && !_disposed)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Changed -= OnFileChanged;
                _watcher.Error -= OnWatcherError;
                _logger.LogInformation("File watcher stopped.");
            }
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (File.Exists(e.FullPath))
                FileDetected?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling file created: {Path}", e.FullPath);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (File.Exists(e.FullPath))
                FileChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling file changed: {Path}", e.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error.");
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            StopWatching();
            _watcher?.Dispose();
            _watcher = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
