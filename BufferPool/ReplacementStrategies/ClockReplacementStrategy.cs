namespace BufferPool.ReplacementStrategies;

public sealed class ClockReplacementStrategy<TKey> : IReplacementStrategy<TKey> where TKey : notnull
{
    private sealed record Node(TKey Key)
    {
        public bool ReferenceBit { get; set; } = true;
        public Node? Next { get; set; }
    }

    private readonly Dictionary<TKey, Node> nodes = [];
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
                node.Next = node; // Self-reference for single node
            }
            else
            {
                // Find the last node in the circular list (the one that points back to the clock hand)
                var current = clockHand;
                while (current.Next != clockHand)
                {
                    current = current.Next;
                }

                // Insert the new node between the last node and the clock hand
                node.Next = clockHand;
                current.Next = node;
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
        _ = nodes.Remove(node.Key);

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
