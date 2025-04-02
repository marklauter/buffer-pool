using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

namespace BufferPool.MemoryMapping;

public readonly ref struct PageLease
{
    public PageLease(
        Span<byte> page,
        SafeMemoryMappedViewHandle handle,
        MemoryMappedViewAccessor view)
    {
        Page = page;
        this.handle = handle;
        this.view = view;
    }

    public readonly Span<byte> Page;
    private readonly SafeMemoryMappedViewHandle handle;
    private readonly MemoryMappedViewAccessor view;

    public void Flush() => view.Flush();

    public void Release() => handle.ReleasePointer();
}
