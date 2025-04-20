using System.IO.MemoryMappedFiles;

namespace BufferPool.MemoryMapping;

public readonly ref struct PageLease
{
    public PageLease(
        Span<byte> page,
        MemoryMappedViewAccessor view)
    {
        Page = page;
        this.view = view;
    }

    public readonly Span<byte> Page;
    private readonly MemoryMappedViewAccessor view;

    public void Flush() => view.Flush();

    public void Release() => view.SafeMemoryMappedViewHandle.ReleasePointer();
}
