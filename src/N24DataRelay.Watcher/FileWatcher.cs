using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using N24DataRelay.Core.Interfaces;

namespace N24DataRelay.Watcher;

/// <summary>Monitors file system for new/changed files.</summary>
public sealed class FileWatcher : IFileWatcher
{
    private readonly ILogger<FileWatcher> _logger;
    private FileSystemWatcher? _watcher;
    private List<FileSystemWatcher>? _watchers;
    private readonly object _disposeLock = new();
    private bool _disposed;

    public event EventHandler<FileSystemEventArgs>? FileDetected;
    public event EventHandler<FileSystemEventArgs>? FileChanged;

    public FileWatcher(ILogger<FileWatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void StartWatching(string path, bool includeSubdirectories, string? filter = null)
    {
        StartWatching(new[] { path }, includeSubdirectories, filter);
    }

    public void StartWatching(IReadOnlyList<string> paths, bool includeSubdirectories, string? filter = null)
    {
        if (_watcher != null || (_watchers != null && _watchers.Count > 0))
            throw new InvalidOperationException("Watcher is already started.");
        if (paths == null || paths.Count == 0)
            throw new ArgumentException("At least one path is required.", nameof(paths));

        var resolvedFilter = string.IsNullOrWhiteSpace(filter) ? "*.*" : filter.Trim();

        if (paths.Count == 1)
        {
            _watcher = new FileSystemWatcher(paths[0])
            {
                Filter                = resolvedFilter,
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = includeSubdirectories,
                EnableRaisingEvents   = true
            };
            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileChanged;
            _watcher.Error += OnWatcherError;
            _logger.LogInformation("File watcher started for path: {Path} (IncludeSubdirectories: {IncludeSub})", paths[0], includeSubdirectories);
            return;
        }

        _watchers = new List<FileSystemWatcher>();
        foreach (var path in paths.Distinct())
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var w = new FileSystemWatcher(path)
            {
                Filter                = resolvedFilter,
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = includeSubdirectories,
                EnableRaisingEvents   = true
            };
            w.Created += OnFileCreated;
            w.Changed += OnFileChanged;
            w.Error += OnWatcherError;
            _watchers.Add(w);
        }
        _logger.LogInformation("File watcher started for {Count} paths (IncludeSubdirectories: {IncludeSub})", _watchers.Count, includeSubdirectories);
    }

    public void StopWatching()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Changed -= OnFileChanged;
                _watcher.Error -= OnWatcherError;
                _watcher = null;
                _logger.LogInformation("File watcher stopped.");
            }
            if (_watchers != null)
            {
                foreach (var w in _watchers)
                {
                    w.EnableRaisingEvents = false;
                    w.Created -= OnFileCreated;
                    w.Changed -= OnFileChanged;
                    w.Error -= OnWatcherError;
                    w.Dispose();
                }
                _watchers = null;
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
            _watchers?.ForEach(w => w.Dispose());
            _watchers = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
