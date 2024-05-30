using System.Diagnostics.CodeAnalysis;

namespace BufferPool;

internal sealed class PageBuffer
    : IAsyncDisposable
    , IDisposable
{
    private readonly ReaderWriterLockSlim gate = new();
    private readonly LinkedList<(int pageId, Page page)> lru; // page id, page
    private readonly FileStream file;
    private readonly int pageSize;
    private readonly int maxPages;

    private PageBuffer(
        FileStream file,
        int pageSize,
        int bufferSizeInKb)
    {
        this.file = file;
        this.pageSize = pageSize;
        maxPages = bufferSizeInKb * 1024 / pageSize;
        lru = new();
        Pages = new(maxPages);
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
            if (TryGetPage(pageId, out var page))
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
            // ensure that the page wasn't added while waiting for the write lock
            if (TryGetPage(pageId, out var page))
            {
                return page;
            }

            if (IsOverflow())
            {
                EvictPage();
            }

            return AddToTop(pageId, LoadPage(pageId));
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    private bool TryGetPage(int pageId, [NotNullWhen(true)] out Page? page)
    {
        if (Pages.TryGetValue(pageId, out page))
        {
            MoveToTop(pageId, page);
            return true;
        }

        return false;
    }

    private Page LoadPage(int pageId)
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

    private void EvictPage()
    {
        var (evictedPageId, _) = lru.Last!.Value;
        lru.RemoveLast();
        _ = Pages.Remove(evictedPageId);
    }

    private bool IsOverflow() => Pages.Count >= maxPages;

    private void MoveToTop(int pageId, Page page)
    {
        _ = lru.Remove((pageId, page));
        _ = lru.AddFirst((pageId, page));
    }

    private Page AddToTop(int pageId, Page page)
    {
        Pages.Add(pageId, page);
        _ = lru.AddFirst((pageId, page));
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        await file.DisposeAsync();
        Dispose();
    }

    private bool disposed;

    public Dictionary<int, Page> Pages { get; }

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
