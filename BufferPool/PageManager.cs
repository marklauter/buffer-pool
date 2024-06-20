using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

public sealed class PageManager
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

    public enum LatchType
    {
        None = 0,
        Read = 1,
        Write = 2,
    }

    internal sealed class Pin(int pageId, byte[] page)
        : IDisposable
    {
        private readonly ReaderWriterLockSlim latch = new();
        public readonly int PageId = pageId;
        public readonly byte[] Page = page;
        public bool IsDirty { get; private set; }

        public static Pin Create(int pageId, byte[] page) => new(pageId, page);

        public Pin SetDirty()
        {
            if (!ThrowIfDisposed().IsWriteLatchHeld)
            {
                throw new InvalidOperationException($"write latch must be held on page before calling {nameof(SetDirty)}");
            }

            IsDirty = true;
            return this;
        }

        public Pin Clean()
        {
            if (!ThrowIfDisposed().IsWriteLatchHeld)
            {
                throw new InvalidOperationException($"write latch must be held on page before calling {nameof(Clean)}");
            }

            IsDirty = false;
            return this;
        }

        public Pin Latch(LatchType lockType) => lockType switch
        {
            LatchType.Read => LatchRead(),
            LatchType.Write => LatchWrite(),
            _ or LatchType.None => throw new InvalidOperationException($"unexpected latch type '{lockType}'"),
        };

        public Pin Unlatch(LatchType lockType) => lockType switch
        {
            LatchType.Read => UnlatchRead(),
            LatchType.Write => UnlatchWrite(),
            _ or LatchType.None => throw new InvalidOperationException($"unexpected latch type '{lockType}'"),
        };

        private Pin LatchRead()
        {
            ThrowIfDisposed().latch.EnterReadLock();
            return this;
        }

        private Pin LatchWrite()
        {
            ThrowIfDisposed().latch.EnterWriteLock();
            return this;
        }

        private Pin UnlatchRead()
        {
            ThrowIfDisposed().latch.ExitReadLock();
            return this;
        }

        private Pin UnlatchWrite()
        {
            ThrowIfDisposed().latch.ExitWriteLock();
            return this;
        }

        public bool IsReadLatchHeld => ThrowIfDisposed().latch.IsReadLockHeld;
        public bool IsWriteLatchHeld => ThrowIfDisposed().latch.IsWriteLockHeld;
        public bool AnyLatchHeld => ThrowIfDisposed().latch.IsReadLockHeld || latch.IsWriteLockHeld;

        private bool disposed;
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            latch.Dispose();

            disposed = true;
        }

        private Pin ThrowIfDisposed() => disposed
            ? throw new ObjectDisposedException(nameof(Pin))
            : this;
    };

    private readonly AsyncLock criticalSection = new();
    private readonly ArrayPool<byte> bufferPool;
    private readonly ConcurrentQueue<Pin> dirtyQueue = new();
    private readonly ConcurrentDictionary<int, Pin> pages;
    private readonly IReplacementPolicy<int> replacementPolicy;

    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int frameSize;
    private bool disposed;

    private PageManager(
        string path,
        PageManagerOptions options,
        IReplacementPolicy<int> replacementPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        pageSize = options.PageSize;
        frameSize = options.FrameSize;
        this.replacementPolicy = replacementPolicy ?? throw new ArgumentNullException(nameof(replacementPolicy));
        file = new FileStream(path, CreateFileStreamOptions(pageSize, frameSize));
        var extraFrameSize = Convert.ToInt32(Math.Round(frameSize + frameSize * 0.25));
        bufferPool = ArrayPool<byte>.Create(pageSize, extraFrameSize);
        pages = new(Environment.ProcessorCount, extraFrameSize);
    }

    public static PageManager FromPath(string path, PageManagerOptions options) =>
        new(path, options, new LruPolicy<int>());

    /// <summary>
    /// ReadThroughAsync bypasses the buffer pool and reads the page straight from the disk.
    /// </summary>
    /// <param name="pageId">the page id</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns><see cref="ValueTask{TResult}"/></returns>
    public ValueTask<byte[]> ReadThroughAsync(int pageId, CancellationToken cancellationToken) =>
        ThrowIfDisposed().LoadAndPinAsync(pageId, LatchType.None, true, cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="latchType"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public ValueTask<byte[]> LeaseAsync(int pageId, LatchType latchType, CancellationToken cancellationToken) =>
        ThrowIfDisposed().TryReadPin(pageId, latchType, out var pin)
            ? ValueTask.FromResult(pin.Page)
            : LoadAndPinAsync(pageId, latchType, false, cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="latchType"></param>
    public void Return(int pageId, LatchType latchType)
    {
        if (ThrowIfDisposed().pages.TryGetValue(pageId, out var pin))
        {
            _ = pin.Unlatch(latchType);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    public void Changed(int pageId)
    {
        if (ThrowIfDisposed().pages.TryGetValue(pageId, out var pin))
        {
            dirtyQueue.Enqueue(pin.SetDirty().Bump(replacementPolicy));
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        var snapshot = ThrowIfDisposed().dirtyQueue.ToArray();
        foreach (var pin in snapshot)
        {
            await FlushPinAsync(pin, cancellationToken);
        }
    }

    public ValueTask FlushAsync(int pageId, CancellationToken cancellationToken) =>
       !ThrowIfDisposed().pages.TryGetValue(pageId, out var pin)
           ? ValueTask.CompletedTask
           : FlushPinAsync(pin, cancellationToken);

    private bool TryReadPin(int pageId, LatchType lockType, [NotNullWhen(true)] out Pin? pin)
    {
        if (pages.TryGetValue(pageId, out pin))
        {
            _ = pin.Latch(lockType).Bump(replacementPolicy);
            return true;
        }

        return false;
    }

    private async ValueTask<byte[]> LoadAndPinAsync(int pageId, LatchType latchType, bool bypassPool, CancellationToken cancellationToken)
    {
        var page = await LoadAsync(pageId, cancellationToken);
        if (!bypassPool)
        {
            EvictIfOverflow(IsOverflow);
            return PinPage(pageId, page, latchType);
        }

        return page;
    }

    private ValueTask<byte[]> LoadAsync(int pageId, CancellationToken cancellationToken)
    {
        var buffer = bufferPool.Rent(pageSize);
        return criticalSection.EnterAsync(async (cancellationToken) =>
        {
            try
            {
                Seek(pageSize * (pageId - 1));
                return await file.ReadAsync(buffer, cancellationToken) != pageSize
                    ? throw new InvalidOperationException($"failed to read page {pageId}")
                    : buffer;
            }
            catch
            {
                bufferPool.Return(buffer);
                throw;
            }
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

    private bool IsOverflow => pages.Count >= frameSize;

    private void EvictIfOverflow(bool isOverflow)
    {
        if (!isOverflow ||
            !replacementPolicy.TryEvict(out var pageId) ||
            !pages.TryGetValue(pageId, out var pin))
        {
            // no reason to or nothing to evict 
            return;
        }

        if (pin.AnyLatchHeld || pin.IsDirty)
        {
            // TryEvict removed the item from the replacement policy, so we need to bump it
            _ = pin?.Bump(replacementPolicy);
            return;
        }

        if (pages.TryRemove(pageId, out _))
        {
            bufferPool.Return(pin.Page);
            pin.Dispose();
        }
    }

    private byte[] PinPage(int pageId, byte[] page, LatchType latchType) =>
        pages.TryAdd(pageId, Pin.Create(pageId, page).Latch(latchType).Bump(replacementPolicy))
            ? page
            : pages.TryGetValue(pageId, out var existingPin)
                ? existingPin.Latch(latchType).Bump(replacementPolicy).Page
                : throw new KeyNotFoundException($"pin not found with page id '{pageId}'");

    private ValueTask FlushPinAsync(Pin pin, CancellationToken cancellationToken) =>
        !pin.IsDirty
            ? ValueTask.CompletedTask
            : !pin.IsWriteLatchHeld
                ? throw new InvalidOperationException($"write latch must be held on pin with page id '{pin.PageId}' before calling {nameof(FlushPinAsync)}")
                : criticalSection.EnterAsync(async (cancellationToken) =>
                {
                    Seek(pageSize * (pin.PageId - 1));
                    await file.WriteAsync(pin.Page, cancellationToken);
                    _ = pin.Clean();
                },
                cancellationToken);

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

        replacementPolicy.Dispose();
        criticalSection.Dispose();

        disposed = true;
    }

    private PageManager ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(Pin))
        : this;
}
