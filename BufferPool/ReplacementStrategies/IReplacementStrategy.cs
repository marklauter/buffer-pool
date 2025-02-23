namespace BufferPool.ReplacementStrategies;

public interface IReplacementStrategy<TKey>
    : IDisposable
    where TKey : notnull
{
    ValueTask BumpAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken);
    ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken);
}
