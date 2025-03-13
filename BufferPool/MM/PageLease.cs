using Microsoft.Win32.SafeHandles;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.MM;

public readonly ref struct PageLease
{
    public PageLease(
        Span<byte> page,
        SafeMemoryMappedViewHandle handle)
    {
        Page = page;
        this.handle = handle;
    }

    public readonly Span<byte> Page;
    private readonly SafeMemoryMappedViewHandle handle;

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected", Justification = "the lease is the new owner of the handle")]
    public void Release()
    {
        handle.ReleasePointer();
        handle.Dispose();
    }
}
