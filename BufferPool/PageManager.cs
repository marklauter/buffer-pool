using BufferPool.ReplacementStrategies;
using System.Buffers;
using System.Collections.Concurrent;

namespace BufferPool;

public sealed class PageManager
    : IAsyncDisposable
    , IDisposable
{
    public static PageManager CreateWithLruReplacementStrategy(string path, PageManagerOptions options) =>
        new(path, options, new LruReplacementStrategy<int>());

    public static FileStreamOptions DefaultFileStreamOptions => new()
    {
        Access = FileAccess.ReadWrite,
        BufferSize = 0,
        Mode = FileMode.OpenOrCreate,
        Share = FileShare.Read,
        Options = FileOptions.RandomAccess | FileOptions.WriteThrough | FileOptions.Asynchronous,
    };

    private static FileStreamOptions CreateFileStreamOptions(int pageSize, int frameSize) => new()
    {
        Access = FileAccess.ReadWrite,
        BufferSize = 0,
        Mode = FileMode.OpenOrCreate,
        Share = FileShare.Read,
        Options = FileOptions.RandomAccess | FileOptions.WriteThrough | FileOptions.Asynchronous,
        PreallocationSize = pageSize * frameSize,
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
    private bool isFileDisposed;

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

    /// <summary>
    /// ReadThroughAsync bypasses the buffer pool and reads the page straight from the disk.
    /// </summary>
    /// <param name="pageId">the page id</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns><see cref="ValueTask{TResult}"/></returns>
    public ValueTask<byte[]> ReadThroughAsync(int pageId, CancellationToken cancellationToken) =>
        ThrowIfDisposed().LoadPageAsync(ValidPageId(pageId), cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="latchType"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<byte[]> LeaseAsync(int pageId, LatchType latchType, CancellationToken cancellationToken) =>
        ThrowIfDisposed().FlushQueueIfRequired().TryReadPin(ValidPageId(pageId), latchType) is (true, var pin)
            ? pin!.Page
            : await PinPageAsync(pageId, latchType, false, cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pageId"></param>
    /// <param name="latchType"></param>
    /// <returns></returns>
    public bool Return(int pageId, LatchType latchType)
    {
        if (ThrowIfDisposed().FlushQueueIfRequired().frames.TryGetValue(ValidPageId(pageId), out var pin))
        {
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
    public bool SetDirty(int pageId)
    {
        if (ThrowIfDisposed().frames.TryGetValue(ValidPageId(pageId), out var pin))
        {
            dirtyQueue.Enqueue(pin.SetDirty().Touch(replacementStrategy));
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
        while (dirtyQueue.TryDequeue(out var pin))
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
       ThrowIfDisposed().frames.TryGetValue(ValidPageId(pageId), out var pin) && await FlushPinAsync(pin, cancellationToken);

    private (bool success, Pin? pin) TryReadPin(int pageId, LatchType lockType) =>
        frames.TryGetValue(pageId, out var pin)
            ? (true, pin.Latch(lockType).Touch(replacementStrategy))
            : (false, null);

    private async ValueTask<byte[]> PinPageAsync(int pageId, LatchType latchType, bool bypassPool, CancellationToken cancellationToken)
    {
        var page = await LoadPageAsync(pageId, cancellationToken);
        if (!bypassPool)
        {
            await EvictIfOverflowAsync(IsOverflow, cancellationToken);
            return PinPage(pageId, page, latchType);
        }

        return page;
    }

    private async ValueTask<byte[]> LoadPageAsync(int pageId, CancellationToken cancellationToken)
    {
        var buffer = bufferPool.Rent(pageSize);
        try
        {
            return await alock.WithLockAsync(async (cancellationToken) =>
            {
                Seek(pageSize * (pageId - 1));
                return await file.ReadAsync(buffer, cancellationToken) != pageSize
                    ? throw new InvalidOperationException($"failed to read page {pageId}")
                    : buffer;
            }, cancellationToken);
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
            !(replacementStrategy.TryEvict() is (true, var pageId)) ||
            !frames.TryGetValue(pageId, out var pin) ||
            pin.AnyLatchHeld ||
            pin.IsDirty && !await FlushPinAsync(pin, cancellationToken))
        {
            // nothing to evict, best eviction cadidate is leased, or best eviction candidate is awaiting flush
            return;
        }

        if (frames.TryRemove(pageId, out _))
        {
            bufferPool.Return(pin.Page);
            pin.Dispose();
        }
    }

    private byte[] PinPage(int pageId, byte[] page, LatchType latchType) =>
        frames.TryGetValue(pageId, out var existingPin) // check exists first to avoid allocation on Pin.Create
            ? existingPin.Latch(latchType).Touch(replacementStrategy).Page
            : frames.TryAdd(pageId, Pin.Create(pageId, page).Latch(latchType).Touch(replacementStrategy))
                ? page
                : throw new Exception($"failed to add pin with page id '{pageId}'");

    private long flushing;
    private PageManager FlushQueueIfRequired()
    {
        if (ThrowIfDisposed().dirtyQueue.IsEmpty || Interlocked.CompareExchange(ref flushing, 1, 0) == 1)
        {
            return this;
        }

        FlushQueue();

        return this;

        async void FlushQueue()
        {
            try
            {
                using var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await Task.Run(async () => await FlushDirtyItemsAsync(source.Token));
            }
            finally
            {
                _ = Interlocked.Exchange(ref flushing, 0);
            }
        }
    }

    public async ValueTask FlushDirtyItemsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
            && dirtyQueue.TryDequeue(out var pin))
        {
            _ = await FlushPinAsync(pin, cancellationToken);
        }
    }

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

    private static int ValidPageId(int pageId) =>
        pageId >= 0
            ? pageId
            : throw new ArgumentOutOfRangeException(nameof(pageId));

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        if (!isFileDisposed)
        {
            await file.DisposeAsync();
            isFileDisposed = true;
        }

        await FlushDirtyItemsAsync(CancellationToken.None);

        Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (!isFileDisposed)
        {
            file.Dispose();
            isFileDisposed = true;
        }

        foreach (var pin in frames.Values)
        {
            bufferPool.Return(pin.Page);
            pin.Dispose();
        }

        frames.Clear();

        alock.Dispose();

        disposed = true;
    }

    private PageManager ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(PageManager))
        : this;
}
