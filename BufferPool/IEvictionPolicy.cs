using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

public interface IEvictionPolicy<T>
{
    void Access(T item);
    void Dispose();
    bool TryEvict([NotNullWhen(true)] out T? evictedItem);
}
