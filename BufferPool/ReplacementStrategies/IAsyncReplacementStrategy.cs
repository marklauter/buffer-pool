namespace BufferPool.ReplacementStrategies;

public interface IAsyncReplacementStrategy<TKey>
    : IDisposable
    where TKey : notnull
{
    ValueTask BumpAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken);
}
