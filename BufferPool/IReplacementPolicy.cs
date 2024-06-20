using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

public interface IReplacementPolicy<T>
{
    void Bump(T item);
    void Dispose();
    bool TryEvict([NotNullWhen(true)] out T? evictedItem);
}
