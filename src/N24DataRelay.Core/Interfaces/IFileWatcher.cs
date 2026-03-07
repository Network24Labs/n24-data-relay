namespace N24DataRelay.Core.Interfaces;

/// <summary>Interface for file system watching.</summary>
public interface IFileWatcher : IDisposable
{
    event EventHandler<FileSystemEventArgs>? FileDetected;
    event EventHandler<FileSystemEventArgs>? FileChanged;
    void StartWatching(string path, bool includeSubdirectories);
    void StopWatching();
}
