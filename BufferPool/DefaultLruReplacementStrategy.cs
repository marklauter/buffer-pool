namespace BufferPool;

// todo: consider LRU-K strategy
// todo: try this idea from copilot
/*
1.	HashMap + Double LinkedList: A common approach to optimize an LRU cache is to use a combination of a hash map and a double-linked list. The hash map stores keys and pointers to nodes in the linked list, which represents the actual order of elements based on their usage. This combination allows for constant-time O(1) access, addition, and removal operations.
•	Access: To access a key, use the hash map to find the corresponding node in the linked list in O(1) time, then move the node to the front of the list to mark it as recently used.
•	Eviction: To evict the least recently used key, remove the node from the end of the linked list and also remove its entry from the hash map, both operations in O(1) time.
 */

public sealed class DefaultLruReplacementStrategy<TKey>
    : IReplacementStrategy<TKey>
    , IDisposable
{
    private readonly LinkedList<TKey> accessList = new();
    private readonly AsyncLock asyncLock = new();
    private bool disposed;

    public ValueTask BumpAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (accessList.First is not null && accessList.First.Value is not null && accessList.First.Value.Equals(key))
            {
                return;
            }

            _ = accessList.Remove(key);
            _ = accessList.AddFirst(key);
        }, cancellationToken);

    public async ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken) =>
        await ThrowIfDisposed().asyncLock.WithLockAsync(() => accessList.Remove(key), cancellationToken);

    public async ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken) =>
        await ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (accessList.Count > 0 && accessList.Last!.Value is TKey evictedKey)
            {
                accessList.RemoveLast();
                return (true, evictedKey);
            }

            return (false, default(TKey));
        }, cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        asyncLock.Dispose();
    }

    private DefaultLruReplacementStrategy<TKey> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(DefaultLruReplacementStrategy<TKey>))
        : this;
}
