using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

public sealed class PageBuffer
    : IAsyncDisposable
    , IDisposable
{
    private sealed class Pin(byte[] page)
    {
        internal byte[] Page { get; } = page;
        internal int References { get; private set; } = 1;
        internal bool Dirty { get; private set; }

        internal static Pin Create(byte[] page) => new(page);

        internal void Lease() => ++References;
        internal void Return() => References = References == 0 ? References : References - 1;
        internal void MarkDirty() => Dirty = true;
    };

    private readonly Latch latch = new();
    private readonly ArrayPool<byte> bufferPool;
    private readonly Dictionary<int, Pin> pages;
    private readonly LruCache<int> lru = new();
    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int frameSize;
    private bool disposed;

    private PageBuffer(
        FileStream file,
        int pageSize,
        int frameSize)
    {
        this.file = file;
        this.pageSize = pageSize;
        this.frameSize = frameSize;
        bufferPool = ArrayPool<byte>.Create(pageSize, frameSize);
        pages = new(frameSize);
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "PageBuffer will dispose of the file.")]
    public static PageBuffer FromPath(string path, int pageSize, int bufferSizeInKb) =>
        new(File.OpenWrite(path), pageSize, bufferSizeInKb);

    public ValueTask<byte[]> LeaseAsync(int pageId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return latch.CriticalSectionAsync(async (cancellationToken) =>
            TryAccess(pageId, out var pin)
                ? pin.Page
                : await LoadAsync(pageId, cancellationToken),
            cancellationToken);
    }

    public Task ReturnAsync(int pageId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return latch.CriticalSectionAsync(() =>
        {
            if (pages.TryGetValue(pageId, out var pin))
            {
                pin.Return();
            }
        }, cancellationToken);
    }

    public Task MakeDirtyAsync(int pageId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return latch.CriticalSectionAsync(() =>
        {
            if (pages.TryGetValue(pageId, out var pin))
            {
                pin.MarkDirty();
            }
        }, cancellationToken);
    }

    private bool TryAccess(int pageId, [NotNullWhen(true)] out Pin? pin)
    {
        if (pages.TryGetValue(pageId, out pin))
        {
            pin.Lease();
            lru.Access(pageId);
            return true;
        }

        return false;
    }

    private async ValueTask<byte[]> LoadAsync(int pageId, CancellationToken cancellationToken)
    {
        if (IfOverflow())
        {
            Evict();
        }

        var page = await ReadPageAsync(pageId, cancellationToken);
        PinPage(pageId, page);

        return page;
    }

    private bool IfOverflow() => pages.Count >= frameSize;

    private void Evict()
    {
        if (!lru.TryEvict(out var pageId))
        {
            return;
        }

        if (!pages.TryGetValue(pageId, out var pin))
        {
            return;
        }

        if (pin.References > 1)
        {
            pin.Return();
            return;
        }

        _ = pages.Remove(pageId);
        bufferPool.Return(pin.Page);
    }

    private async ValueTask<byte[]> ReadPageAsync(int pageId, CancellationToken cancellationToken)
    {
        Seek(pageSize * (pageId - 1));
        var buffer = bufferPool.Rent(pageSize);
        return await file.ReadAsync(buffer, cancellationToken) != pageSize
            ? throw new InvalidOperationException($"failed to read page {pageId}")
            : buffer;
    }

    public void Seek(int offset)
    {
        if (file.Seek(offset, SeekOrigin.Begin) != offset)
        {
            throw new InvalidOperationException($"failed to seek to {offset}");
        }
    }

    private void PinPage(int pageId, byte[] page)
    {
        var pin = Pin.Create(page);
        pages.Add(pageId, pin);
        lru.Access(pageId);
    }

    public async ValueTask DisposeAsync()
    {
        await file.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        latch.Dispose();

        disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
