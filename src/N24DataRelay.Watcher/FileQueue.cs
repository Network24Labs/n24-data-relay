using System.Collections.Concurrent;

namespace N24DataRelay.Watcher;

/// <summary>Thread-safe queue for file transfer paths with stability tracking.</summary>
public interface IFileQueue
{
    bool TryEnqueue(string filePath);
    bool TryPeek(out string? filePath);
    void Remove(string filePath);
    int Count { get; }
    void UpdateFileActivity(string filePath);
    bool IsFileStable(string filePath, int stabilitySeconds);
}

public sealed class FileQueue : IFileQueue
{
    private readonly ConcurrentDictionary<string, FileQueueItem> _files = new();
    private readonly object _peekLock = new();

    public int Count => _files.Count;

    public bool TryEnqueue(string filePath)
    {
        var item = new FileQueueItem
        {
            FilePath = filePath,
            AddedTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };
        return _files.TryAdd(filePath, item);
    }

    public bool TryPeek(out string? filePath)
    {
        lock (_peekLock)
        {
            var oldest = _files.Values.OrderBy(f => f.AddedTime).FirstOrDefault();
            if (oldest != null)
            {
                filePath = oldest.FilePath;
                return true;
            }
            filePath = null;
            return false;
        }
    }

    public void Remove(string filePath) => _files.TryRemove(filePath, out _);

    public void UpdateFileActivity(string filePath)
    {
        if (_files.TryGetValue(filePath, out var item))
            item.LastActivity = DateTime.UtcNow;
    }

    public bool IsFileStable(string filePath, int stabilitySeconds)
    {
        if (_files.TryGetValue(filePath, out var item))
            return (DateTime.UtcNow - item.LastActivity).TotalSeconds >= stabilitySeconds;
        return true;
    }

    private sealed class FileQueueItem
    {
        public required string FilePath { get; init; }
        public DateTime AddedTime { get; set; }
        public DateTime LastActivity { get; set; }
    }
}
