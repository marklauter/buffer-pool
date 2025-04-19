namespace BufferPool.ReplacementStrategies;

public interface IReplacementStrategy<TKey>
    where TKey : notnull
{
    void Touch(TKey key);
    bool TryEvict(TKey key);
    (bool wasEvicted, TKey evictedKey) TryEvict();
}
