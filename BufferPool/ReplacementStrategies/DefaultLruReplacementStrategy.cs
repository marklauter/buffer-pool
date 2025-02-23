namespace BufferPool.ReplacementStrategies;

public sealed class DefaultLruReplacementStrategy<TKey>
    : IReplacementStrategy<TKey>
    , IDisposable
    where TKey : notnull
{
    private readonly LinkedList<TKey> accessList = new();
    private readonly AsyncLock asyncLock = new();
    private bool disposed;

    public ValueTask BumpAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (accessList.First is not null && accessList.First.Value is not null && accessList.First.Value.Equals(key))
                return;

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
            return;

        disposed = true;
        asyncLock.Dispose();
    }

    private DefaultLruReplacementStrategy<TKey> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(DefaultLruReplacementStrategy<TKey>))
        : this;
}
