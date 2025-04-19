using System.Runtime.CompilerServices;

namespace BufferPool.ReplacementStrategies;

public sealed class LruReplacementStrategy<TKey>
    : IReplacementStrategy<TKey>
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
    private readonly object gate = new();
    private Node? head;
    private Node? tail;

    public void Touch(TKey key)
    {
        lock (gate)
        {
            if (head != null && head.Key.Equals(key))
                return;

            if (accessMap.TryGetValue(key, out var node))
            {
                Detach(node);
                ToFirst(node);
                return;
            }

            node = Node.WithKey(key);
            accessMap.Add(key, node);
            ToFirst(node);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEvict(TKey key)
    {
        lock (gate)
        {
            return accessMap.TryGetValue(key, out var node) && Remove(node);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (bool wasEvicted, TKey evictedKey) TryEvict()
    {
        lock (gate)
        {
            var node = tail;
            return node == null
                ? (false, default!)
                : (Remove(node), node.Key);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ToFirst(Node node)
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
    private void Detach(Node node)
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
    private bool Remove(Node node)
    {
        if (accessMap.Remove(node.Key))
        {
            Detach(node);
            return true;
        }

        return false;
    }
}

