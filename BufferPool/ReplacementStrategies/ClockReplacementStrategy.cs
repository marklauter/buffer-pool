namespace BufferPool.ReplacementStrategies;

public sealed class ClockReplacementStrategy<TKey> : IReplacementStrategy<TKey> where TKey : notnull
{
    private sealed class Node
    {
        public TKey Key { get; }
        public bool ReferenceBit { get; set; }
        public Node? Next { get; set; }

        public Node(TKey key)
        {
            Key = key;
            ReferenceBit = true;
        }
    }

    private readonly Dictionary<TKey, Node> nodes = new();
    private readonly AsyncLock asyncLock = new();
    private Node? clockHand;
    private bool disposed;

    public ValueTask BumpAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (nodes.TryGetValue(key, out var node))
            {
                node.ReferenceBit = true;
                return;
            }

            node = new Node(key);
            nodes.Add(key, node);

            if (clockHand == null)
            {
                clockHand = node;
                node.Next = node;
            }
            else
            {
                node.Next = clockHand.Next;
                clockHand.Next = node;
            }
        }, cancellationToken);

    public ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (!nodes.TryGetValue(key, out var node))
                return false;

            RemoveNode(node);
            return true;
        }, cancellationToken);

    public ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (clockHand == null)
                return (false, default!);

            while (true)
            {
                if (!clockHand.ReferenceBit)
                {
                    var key = clockHand.Key;
                    RemoveNode(clockHand);
                    return (true, key);
                }

                clockHand.ReferenceBit = false;
                clockHand = clockHand.Next;
            }
        }, cancellationToken);

    private void RemoveNode(Node node)
    {
        nodes.Remove(node.Key);

        if (nodes.Count == 0)
        {
            clockHand = null;
            return;
        }

        // Find predecessor
        var pred = clockHand;
        while (pred!.Next != node)
        {
            pred = pred.Next;
        }

        // Remove node from circular list
        pred.Next = node.Next;

        // Update clock hand if needed
        if (clockHand == node)
        {
            clockHand = node.Next;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        asyncLock.Dispose();
        disposed = true;
    }

    private ClockReplacementStrategy<TKey> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(ClockReplacementStrategy<TKey>))
        : this;
}
