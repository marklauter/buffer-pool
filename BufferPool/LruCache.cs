using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

internal sealed class LruCache<T>
{
    // todo: try this idea from copilot
    /*
1.	HashMap + Double LinkedList: A common approach to optimize an LRU cache is to use a combination of a hash map and a double-linked list. The hash map stores keys and pointers to nodes in the linked list, which represents the actual order of elements based on their usage. This combination allows for constant-time O(1) access, addition, and removal operations.
•	Access: To access an item, use the hash map to find the corresponding node in the linked list in O(1) time, then move the node to the front of the list to mark it as recently used.
•	Eviction: To evict the least recently used item, remove the node from the end of the linked list and also remove its entry from the hash map, both operations in O(1) time.
     */
    private readonly LinkedList<T> accessList = new();

    public void Access(T item)
    {
        _ = accessList.Remove(item);
        _ = accessList.AddFirst(item);
    }

    public bool TryEvict([NotNullWhen(true)] out T? evictedItem)
    {
        evictedItem = default;
        if (accessList.Count == 0)
        {
            return false;
        }

        evictedItem = accessList.Last!.Value!;
        accessList.RemoveLast();
        return true;
    }
}
