namespace N24DataRelay.Core.Interfaces;

/// <summary>Interface for file system watching.</summary>
public interface IFileWatcher : IDisposable
{
    event EventHandler<FileSystemEventArgs>? FileDetected;
    event EventHandler<FileSystemEventArgs>? FileChanged;
    /// <param name="filter">
    ///   FileSystemWatcher-style pattern (single wildcard, e.g. <c>*.csv</c> or <c>*.*</c>).
    ///   Defaults to <c>*.*</c> (all files) when null or empty.
    /// </param>
    void StartWatching(string path, bool includeSubdirectories, string? filter = null);
    void StopWatching();
}
