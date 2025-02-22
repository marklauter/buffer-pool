namespace BufferPool;

public interface IReplacementStrategy<TKey>
    : IDisposable
{
    ValueTask BumpAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken);
}
