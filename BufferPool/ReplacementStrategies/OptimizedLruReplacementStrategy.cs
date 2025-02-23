using System.Runtime.CompilerServices;

namespace BufferPool.ReplacementStrategies;

public sealed class OptimizedLruReplacementStrategy<TKey>
    : IReplacementStrategy<TKey>
    , IDisposable
    where TKey : notnull
{
    private sealed record Node(TKey Key)
    {
        public Node? Prev { get; set; }
        public Node? Next { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Node WithKey(TKey key) => new(key);
    }

    private readonly Dictionary<TKey, Node> accessMap = [];
    private readonly AsyncLock asyncLock = new();
    private Node? head;
    private Node? tail;
    private bool disposed;

    public ValueTask BumpAsync(TKey item, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (accessMap.TryGetValue(item, out var node))
            {
                DetachNode(node);
                MoveToFront(node);
            }
            else
            {
                node = Node.WithKey(item);
                accessMap.Add(item, node);
                MoveToFront(node);
            }
        }, cancellationToken);

    public ValueTask<bool> TryEvictAsync(TKey item, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (accessMap.TryGetValue(item, out var node))
            {
                DetachNode(node);
                return accessMap.Remove(item);
            }

            return false;
        }, cancellationToken);

#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type. Justification: It doesn't matter what the return value of the evicted key is when wasEvicted is false.
    public ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            var node = tail;
            return node == null
                ? (false, default)
                : (RemoveNode(node), node.Key);
        }, cancellationToken);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveToFront(Node node)
    {
        node.Next = head;
        node.Prev = null;

        if (head != null)
            head.Prev = node;

        head = node;

        if (tail == null)
            tail = node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DetachNode(Node node)
    {
        if (node.Prev != null)
            node.Prev.Next = node.Next;
        else
            head = node.Next;

        if (node.Next != null)
            node.Next.Prev = node.Prev;
        else
            tail = node.Prev;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RemoveNode(Node node)
    {
        if (accessMap.Remove(node.Key))
        {
            DetachNode(node);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        asyncLock.Dispose();
        disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private OptimizedLruReplacementStrategy<TKey> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(OptimizedLruReplacementStrategy<TKey>))
        : this;
}
