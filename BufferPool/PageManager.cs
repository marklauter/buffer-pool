using System.Buffers;
using System.Collections.Concurrent;

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

    private readonly AsyncLock alock = new();
    private readonly ArrayPool<byte> bufferPool;
    private readonly ConcurrentQueue<Pin> dirtyQueue = new();
    private readonly ConcurrentDictionary<int, Pin> frames;
    private readonly IReplacementStrategy<int> replacementStrategy;

    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int frameCapacity;
    private bool disposed;

    private PageManager(
        string path,
        PageManagerOptions options,
        IReplacementStrategy<int> replacementStrategy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentNullException.ThrowIfNull(options);

        pageSize = options.PageSize;
        frameCapacity = options.FrameCapacity;
        this.replacementStrategy = replacementStrategy ?? throw new ArgumentNullException(nameof(replacementStrategy));
        file = new FileStream(path, CreateFileStreamOptions(pageSize, frameCapacity));
        var extraFrameCapacity = Convert.ToInt32(Math.Round(frameCapacity + frameCapacity * 0.25));
        bufferPool = ArrayPool<byte>.Create(pageSize, extraFrameCapacity);
        frames = new(Environment.ProcessorCount, extraFrameCapacity);
    }

    public static PageManager CreateWithLruStrategy(string path, PageManagerOptions options) =>
        new(path, options, new LruStrategy<int>());

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
    public async ValueTask<byte[]> LeaseAsync(int pageId, LatchType latchType, CancellationToken cancellationToken) =>
        await ThrowIfDisposed().TryReadPinAsync(pageId, latchType, cancellationToken) is (true, var pin)
            ? pin!.Page
            : await LoadAndPinAsync(pageId, latchType, false, cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="latchType"></param>
    /// <returns></returns>
    public bool Return(int pageId, LatchType latchType)
    {
        if (ThrowIfDisposed().frames.TryGetValue(pageId, out var pin))
        {
            // todo: what if the page is dirty and latch type is write? throw exception?
            _ = pin.Unlatch(latchType);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <returns></returns>
    public async Task<bool> SetDirtyAsync(int pageId, CancellationToken cancellationToken)
    {
        if (ThrowIfDisposed().frames.TryGetValue(pageId, out var pin))
        {
            dirtyQueue.Enqueue(await pin.SetDirty().BumpAsync(replacementStrategy, cancellationToken));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (ThrowIfDisposed().dirtyQueue.IsEmpty)
        {
            return;
        }

        List<Exception>? exceptions = null;
        var snapshot = dirtyQueue.ToArray();
        foreach (var pin in snapshot)
        {
            try
            {
                _ = await FlushPinAsync(pin, cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    public async ValueTask<bool> FlushAsync(int pageId, CancellationToken cancellationToken) =>
       ThrowIfDisposed().frames.TryGetValue(pageId, out var pin) && await FlushPinAsync(pin, cancellationToken);

    private async ValueTask<(bool success, Pin? pin)> TryReadPinAsync(int pageId, LatchType lockType, CancellationToken cancellationToken)
    {
        if (frames.TryGetValue(pageId, out var pin))
        {
            _ = await pin.Latch(lockType).BumpAsync(replacementStrategy, cancellationToken);
            return (true, pin);
        }

        return (false, null);
    }

    private async ValueTask<byte[]> LoadAndPinAsync(int pageId, LatchType latchType, bool bypassPool, CancellationToken cancellationToken)
    {
        var page = await LoadAsync(pageId, cancellationToken);
        if (!bypassPool)
        {
            await EvictIfOverflowAsync(IsOverflow, cancellationToken);
            return await PinPageAsync(pageId, page, latchType, cancellationToken);
        }

        return page;
    }

    private async ValueTask<byte[]> LoadAsync(int pageId, CancellationToken cancellationToken)
    {
        var buffer = bufferPool.Rent(pageSize);
        try
        {
            using var scope = await alock.LockAsync(cancellationToken);
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
    }

    private void Seek(int offset)
    {
        if (file.Seek(offset, SeekOrigin.Begin) != offset)
        {
            throw new InvalidOperationException($"failed to seek to {offset}");
        }
    }

    private bool IsOverflow => frames.Count >= frameCapacity;

    private async ValueTask EvictIfOverflowAsync(bool isOverflow, CancellationToken cancellationToken)
    {
        if (!isOverflow ||
            !(await replacementStrategy.TryEvictAsync(cancellationToken) is (true, var pageId)) ||
            !frames.TryGetValue(pageId, out var pin))
        {
            // no reason to or nothing to evict 
            return;
        }

        if (pin.AnyLatchHeld || pin.IsDirty)
        {
            // TryEvict removed the item from the replacement strategy, so we need to bump it
            _ = pin?.BumpAsync(replacementStrategy, cancellationToken);
            return;
        }

        if (frames.TryRemove(pageId, out _))
        {
            bufferPool.Return(pin.Page);
            pin.Dispose();
        }
    }

    private async ValueTask<byte[]> PinPageAsync(int pageId, byte[] page, LatchType latchType, CancellationToken cancellationToken) =>
        frames.TryGetValue(pageId, out var existingPin) // check exists first to avoid allocation on Pin.Create
            ? (await existingPin.Latch(latchType).BumpAsync(replacementStrategy, cancellationToken)).Page
            : frames.TryAdd(pageId, await Pin.Create(pageId, page).Latch(latchType).BumpAsync(replacementStrategy, cancellationToken))
                ? page
                : throw new Exception($"failed to add pin with page id '{pageId}'");

    private async ValueTask<bool> FlushPinAsync(Pin pin, CancellationToken cancellationToken) =>
        pin.IsDirty &&
        pin.IsWriteLatchHeld &&
        await alock.WithLockAsync(async (cancellationToken) =>
        {
            Seek(pageSize * (pin.PageId - 1));
            await file.WriteAsync(pin.Page, cancellationToken);
            _ = pin.Clean();
            return true;
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

        replacementStrategy.Dispose();
        alock.Dispose();

        disposed = true;
    }

    private PageManager ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(Pin))
        : this;
}
