namespace BufferPool;

// todo: consider LRU-K strategy
// todo: try this idea from copilot
/*
1.	HashMap + Double LinkedList: A common approach to optimize an LRU cache is to use a combination of a hash map and a double-linked list. The hash map stores keys and pointers to nodes in the linked list, which represents the actual order of elements based on their usage. This combination allows for constant-time O(1) access, addition, and removal operations.
•	Access: To access an item, use the hash map to find the corresponding node in the linked list in O(1) time, then move the node to the front of the list to mark it as recently used.
•	Eviction: To evict the least recently used item, remove the node from the end of the linked list and also remove its entry from the hash map, both operations in O(1) time.
 */

internal sealed class LruStrategy<T>
    : IReplacementStrategy<T>
    , IDisposable
{
    private readonly LinkedList<T> accessList = new();
    private readonly AsyncLock alock = new();
    private bool disposed;

    public ValueTask BumpAsync(T item, CancellationToken cancellationToken) =>
        ThrowIfDisposed().alock.WithLockAsync(() =>
        {
            if (accessList.First is not null && accessList.First.Value is not null && accessList.First.Value.Equals(item))
            {
                return;
            }

            _ = accessList.Remove(item);
            _ = accessList.AddFirst(item);
        }, cancellationToken);

    public async ValueTask<bool> TryEvictAsync(T item, CancellationToken cancellationToken) =>
        await ThrowIfDisposed().alock.WithLockAsync(() => accessList.Remove(item), cancellationToken);

    public async ValueTask<(bool evicted, T? evictedItem)> TryEvictAsync(CancellationToken cancellationToken) =>
        await ThrowIfDisposed().alock.WithLockAsync(() =>
        {
            if (accessList.Count > 0 && accessList.Last!.Value is T evictedItem)
            {
                accessList.RemoveLast();
                return (true, evictedItem);
            }

            return (false, default(T));
        }, cancellationToken);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        alock.Dispose();

        disposed = true;
    }

    private LruStrategy<T> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(LruStrategy<T>))
        : this;
}
