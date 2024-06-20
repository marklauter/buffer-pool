namespace BufferPool;

public interface IReplacementStrategy<T>
    : IDisposable
{
    ValueTask BumpAsync(T item, CancellationToken cancellationToken);
    ValueTask<bool> TryEvictAsync(T item, CancellationToken cancellationToken);
    ValueTask<(bool evicted, T? evictedItem)> TryEvictAsync(CancellationToken cancellationToken);

}
