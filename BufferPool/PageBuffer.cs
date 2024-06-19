using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

// todo: need to kick off a background thread to write dirty pages to disk on evictions see the MS memory cache for sample
public sealed class PageBuffer
    : IAsyncDisposable
    , IDisposable
{
    private static FileStreamOptions CreateFileStreamOptions(int pageSize, int frameSize) => new()
    {
        Access = FileAccess.ReadWrite,
        BufferSize = pageSize,
        Mode = FileMode.OpenOrCreate,
        Share = FileShare.Read,
        Options = FileOptions.RandomAccess | FileOptions.WriteThrough | FileOptions.Asynchronous,
        PreallocationSize = pageSize * frameSize
    };

    private sealed class Pin(int pageId, byte[] page)
    {
        public readonly int PageId = pageId;
        public readonly byte[] Page = page;
        private long references = 1;
        public long References => Interlocked.Read(ref references);
        public bool Changed;

        public static Pin Create(int pageId, byte[] page) => new(pageId, page);

        public void Lease() => Interlocked.Increment(ref references);
        public void Return() => Interlocked.Decrement(ref references);
    };

    private readonly Latch latch = new();
    private readonly ArrayPool<byte> bufferPool;
    private readonly ConcurrentQueue<Pin> dirtyPins = new();
    private readonly ConcurrentDictionary<int, Pin> pages;
    private readonly IEvictionPolicy<int> evictionPolicy;

    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int frameSize;
    private bool disposed;

    private PageBuffer(
        string path,
        int pageSize,
        int frameSize,
        IEvictionPolicy<int> evictionPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        this.pageSize = pageSize;
        this.frameSize = frameSize;
        this.evictionPolicy = evictionPolicy ?? throw new ArgumentNullException(nameof(evictionPolicy));
        file = new FileStream(path, CreateFileStreamOptions(pageSize, frameSize));
        bufferPool = ArrayPool<byte>.Create(pageSize, frameSize);
        pages = new(Environment.ProcessorCount, frameSize);
    }

    public static PageBuffer FromPath(string path, int pageSize, int frameSize) =>
        new(path, pageSize, frameSize, new LruCache<int>());

    public ValueTask<byte[]> ReadAsync(int pageId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return LoadAsync(pageId, true, cancellationToken);
    }

    public ValueTask<byte[]> LeaseAsync(int pageId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return TryAccess(pageId, out var pin)
            ? ValueTask.FromResult(pin.Page)
            : LoadAsync(pageId, false, cancellationToken);
    }

    public void Return(int pageId)
    {
        ThrowIfDisposed();

        if (pages.TryGetValue(pageId, out var pin))
        {
            pin.Return();
        }
    }

    public void MakeDirty(int pageId)
    {
        ThrowIfDisposed();

        if (pages.TryGetValue(pageId, out var pin))
        {
            pin.Changed = true;
            dirtyPins.Enqueue(pin);
        }
    }

    private bool TryAccess(int pageId, [NotNullWhen(true)] out Pin? pin)
    {
        if (pages.TryGetValue(pageId, out pin))
        {
            pin.Lease();
            evictionPolicy.Access(pageId);
            return true;
        }

        return false;
    }

    private async ValueTask<byte[]> LoadAsync(int pageId, bool bypass, CancellationToken cancellationToken)
    {
        var page = await ReadPageAsync(pageId, cancellationToken);
        if (!bypass)
        {
            if (IfOverflow())
            {
                Evict();
            }

            PinPage(pageId, page);
        }

        return page;
    }

    private ValueTask<byte[]> ReadPageAsync(int pageId, CancellationToken cancellationToken)
    {
        var buffer = bufferPool.Rent(pageSize);
        return latch.CriticalSectionAsync(async (cancellationToken) =>
        {
            Seek(pageSize * (pageId - 1));
            return await file.ReadAsync(buffer, cancellationToken) != pageSize
                ? throw new InvalidOperationException($"failed to read page {pageId}")
                : buffer;
        },
        cancellationToken);
    }

    private void Seek(int offset)
    {
        if (file.Seek(offset, SeekOrigin.Begin) != offset)
        {
            throw new InvalidOperationException($"failed to seek to {offset}");
        }
    }

    private bool IfOverflow() => pages.Count >= frameSize;

    private void Evict()
    {
        if (!evictionPolicy.TryEvict(out var pageId))
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

        if (pin.Changed)
        {
            return;
        }

        _ = pages.TryRemove(pageId, out _);
        bufferPool.Return(pin.Page);
    }

    private void PinPage(int pageId, byte[] page)
    {
        _ = pages.TryAdd(pageId, Pin.Create(pageId, page));
        evictionPolicy.Access(pageId);
    }

    private async ValueTask WritePageAsync(Pin pin, CancellationToken cancellationToken)
    {
        // todo: need to latch the specific page while writing to prevent concurrent writes
        await latch.CriticalSectionAsync((cancellationToken) =>
        {
            Seek(pageSize * (pin.PageId - 1));
            return file.WriteAsync(pin.Page, cancellationToken);
        },
        cancellationToken);

        pin.Changed = false;
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

        evictionPolicy.Dispose();
        latch.Dispose();

        disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
