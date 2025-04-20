using System.IO.MemoryMappedFiles;

namespace BufferPool.MemoryMapping;

public sealed class PageFile
    : IDisposable
{
    public long FileSize { get; }

    private readonly string filePath;
    private readonly string fileName;
    private readonly int pageSize;
    private readonly FileStream fileStream;

    private readonly MemoryMappedFile memoryMap;
    private readonly List<MemoryMappedViewAccessor> views = [];
    private bool disposed;
    private const int MinPageSize = 4 * 1024;

    public PageFile(string filePath)
        : this(filePath, MinPageSize * 4, MinPageSize)
    {
    }

    public PageFile(
        string filePath,
        long initialSize,
        int pageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, MinPageSize);

        this.filePath = filePath;
        this.pageSize = pageSize;
        fileName = Path.GetFileName(filePath);
        fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        FileSize = EnsureInitialFileSize(EnsureFileSizeIsMultipleOfChunkSize(initialSize, pageSize));
        memoryMap = MemoryMappedFile.CreateFromFile(
            fileStream,
            $"{fileName}_pages",
            0,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            true);

        CreateMappings(FileSize);
    }

    private static long EnsureFileSizeIsMultipleOfChunkSize(long fileSize, int chunkSize) =>
        (fileSize + chunkSize - 1) / chunkSize * chunkSize;

    private long EnsureInitialFileSize(long initialSize)
    {
        if (fileStream.Length < initialSize)
            fileStream.SetLength(initialSize);

        return fileStream.Length;
    }

    private void CreateMappings(long fileSize)
    {
        var currentChunks = (long)views.Count;
        var neededChunks = (fileSize + pageSize - 1) / pageSize;

        for (var chunk = currentChunks; chunk < neededChunks; chunk++)
        {
            var view = memoryMap.CreateViewAccessor(
                chunk * pageSize,
                pageSize,
                MemoryMappedFileAccess.ReadWrite);

            views.Add(view);
        }
    }

    public long Grow()
    {
        ThrowIfDisposed();
        var newSize = IncreaseFileSize(FileSize * 2);
        CreateMappings(newSize);
        return newSize;
    }

    private long IncreaseFileSize(long newSize)
    {
        fileStream.SetLength(newSize);
        return fileStream.Length;
    }

    public unsafe PageLease LeasePage(long offset)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);

        var pageIndex = (int)offset / pageSize;
        var pageOffset = (int)offset % pageSize;

        if (pageIndex >= views.Count)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset exceeds mapped region.");

        byte* ptr = null;
        var view = views[pageIndex];
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

        // todo: a different idea: https://blog.stephencleary.com/2023/09/memory-mapped-files-overlaid-structs.html

        return new PageLease(new Span<byte>(ptr + pageOffset, pageSize), view);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var view in views)
            view.Dispose();

        memoryMap.Dispose();
        fileStream.Dispose();
        views.Clear();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
