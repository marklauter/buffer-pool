using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

internal sealed class LruCache
{
    // todo: try this idea from copilot
    /*
1.	HashMap + Double LinkedList: A common approach to optimize an LRU cache is to use a combination of a hash map and a double-linked list. The hash map stores keys and pointers to nodes in the linked list, which represents the actual order of elements based on their usage. This combination allows for constant-time O(1) access, addition, and removal operations.
•	Access: To access an item, use the hash map to find the corresponding node in the linked list in O(1) time, then move the node to the front of the list to mark it as recently used.
•	Eviction: To evict the least recently used item, remove the node from the end of the linked list and also remove its entry from the hash map, both operations in O(1) time.
     */
    private readonly LinkedList<Page> accessList = new();

    public void Access(Page page)
    {
        _ = accessList.Remove(page);
        _ = accessList.AddFirst(page);
    }

    public bool TryEvict([NotNullWhen(true)] out Page? evictedPage)
    {
        evictedPage = default;
        if (accessList.Count == 0)
        {
            return false;
        }

        evictedPage = accessList.Last!.Value;
        accessList.RemoveLast();

        return true;
    }
}
