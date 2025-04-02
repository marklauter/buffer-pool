using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BufferPool.ReplacementStrategies;

[DebuggerDisplay("{clockHand}, {tail}")]
public sealed class ClockReplacementStrategy<TKey> : IReplacementStrategy<TKey> where TKey : notnull
{
    [DebuggerDisplay("{Key} {IsReferenced} ")]
    private sealed record Node(TKey Key)
    {
        public Node(TKey key, Node? next)
            : this(key) =>
            Next = next;

        public bool IsReferenced { get; set; } = true;
        public Node? Next { get; set; }
    }

    private readonly Dictionary<TKey, Node> nodes = [];
    private readonly AsyncLock asyncLock = new();
    private Node? tail;
    private Node? clockHand;
    private bool disposed;

    public ValueTask BumpAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (nodes.TryGetValue(key, out var node))
            {
                node.IsReferenced = true;
                return;
            }

            InsertNode(key);
        }, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertNode(TKey key)
    {
        var isNotEmpty = NotEmpty();
        var node = AddNode(key, CreateNode(key, isNotEmpty));
        if (isNotEmpty)
        {
            tail = tail!.Next = node;
            return;
        }

        clockHand = tail = node;
        clockHand.Next = node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Node CreateNode(TKey key, bool isNotEmpty)
        => isNotEmpty
            ? new Node(key, clockHand)
            : new Node(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Node AddNode(TKey key, Node node)
    {
        nodes.Add(key, node);
        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Empty() => clockHand is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool NotEmpty() => clockHand is not null;

    public ValueTask<bool> TryEvictAsync(TKey key, CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(()
            => nodes.TryGetValue(key, out var node)
            && RemoveNode(key, node), cancellationToken);

    public ValueTask<(bool wasEvicted, TKey evictedKey)> TryEvictAsync(CancellationToken cancellationToken) =>
        ThrowIfDisposed().asyncLock.WithLockAsync(() =>
        {
            if (Empty())
                return (false, default!);

            while (true)
            {
                if (!clockHand!.IsReferenced)
                {
                    var key = clockHand.Key;
                    return (RemoveNode(key, clockHand), key);
                }

                Tick();
            }
        }, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Tick()
    {
        clockHand!.IsReferenced = false;
        clockHand = clockHand.Next;
    }

    private bool RemoveNode(TKey key, Node node)
    {
        var result = nodes.Remove(key);
        if (nodes.Count == 0)
        {
            clockHand = tail = null;
            return result;
        }

        var predecessor = Slice(node);
        if (tail == node)
        {
            tail = predecessor;
        }

        if (clockHand == node)
        {
            clockHand = node.Next;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Node Slice(Node node)
    {
        var predecessor = FindPredecessor(node);
        predecessor.Next = node.Next;
        return predecessor;
    }

    private Node FindPredecessor(Node node)
    {
        if (clockHand!.Next == node)
        {
            return clockHand;
        }

        var candidate = clockHand!.Next;
        while (candidate != clockHand)
        {
            if (candidate!.Next == node)
            {
                return candidate;
            }

            candidate = candidate.Next;
        }

        throw new InvalidOperationException("Predecessor not found.");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        asyncLock.Dispose();
        disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ClockReplacementStrategy<TKey> ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(ClockReplacementStrategy<TKey>))
        : this;
}
