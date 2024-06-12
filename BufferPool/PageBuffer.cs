using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

internal sealed class PageBuffer
    : IAsyncDisposable
    , IDisposable
{
    private readonly ReaderWriterLockSlim gate = new();
    private readonly LruCache lru = new();
    private readonly Dictionary<int, Page> pages;
    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int maxPages;
    private bool disposed;

    private PageBuffer(
        FileStream file,
        int pageSize,
        int bufferSizeInKb)
    {
        this.file = file;
        this.pageSize = pageSize;
        maxPages = bufferSizeInKb * 1024 / pageSize;
        pages = new(maxPages);
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "PageBuffer will dispose of the file.")]
    public static PageBuffer FromPath(string path, int pageSize, int bufferSizeInKb) =>
        new(File.OpenWrite(path), pageSize, bufferSizeInKb);

    public Page this[int pageId] => ReadPage(pageId);

    private Page ReadPage(int pageId)
    {
        ThrowIfDisposed();

        gate.EnterReadLock();
        try
        {
            if (TryAccess(pageId, out var page))
            {
                return page;
            }
        }
        finally
        {
            gate.ExitReadLock();
        }

        gate.EnterWriteLock();
        try
        {
            // double-checked cache pattern
            if (TryAccess(pageId, out var page))
            {
                return page;
            }

            if (IfOverflow())
            {
                Evict();
            }

            return Access(pageId, Load(pageId));
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    private bool TryAccess(int pageId, [NotNullWhen(true)] out Page? page)
    {
        if (pages.TryGetValue(pageId, out page))
        {
            lru.Access(pageId, page);
            return true;
        }

        return false;
    }

    private bool IfOverflow() => pages.Count >= maxPages;

    private void Evict()
    {
        if (lru.TryEvict(out var evictedPage))
        {
            _ = pages.Remove(evictedPage.Value.pageId);
        }
    }

    private Page Load(int pageId)
    {
        var size = pageSize;
        var offset = size * (pageId - 1);
        if (file.Seek(offset, SeekOrigin.Begin) != offset)
        {
            throw new InvalidOperationException($"failed to seek to {offset}");
        }

        var buffer = new byte[size];
        return file.Read(buffer) != size
            ? throw new InvalidOperationException($"failed to read page {pageId}")
            : new Page(buffer);
    }

    private Page Access(int pageId, Page page)
    {
        pages.Add(pageId, page);
        lru.Access(pageId, page);
        return page;
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

        gate.Dispose();

        disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
