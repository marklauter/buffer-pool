using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

namespace BufferPool.MM;

public sealed class ChunkedMappedFile
    : IDisposable
{
    private readonly string filePath;
    private readonly string fileName;
    private readonly List<MemoryMappedFile> mappings = [];
    private readonly List<MemoryMappedViewAccessor> views = [];
    private readonly int chunkSize;
    private readonly long fileSize;
    private bool disposed;
    private const int MinChunkSize = 64 * 1024;

    public ChunkedMappedFile(string filePath)
        : this(filePath, 0)
    {
    }

    public ChunkedMappedFile(
        string filePath,
        long initialSize,
        int chunkSize = MinChunkSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, MinChunkSize);

        this.filePath = filePath;
        this.chunkSize = chunkSize;
        fileName = Path.GetFileName(filePath);
        fileSize = EnsureInitialFileSize(filePath, EnsureFileSizeIsMultipleOfChunkSize(initialSize, chunkSize));

        CreateMappingsForSize(fileSize);
    }

    private static long EnsureFileSizeIsMultipleOfChunkSize(long fileSize, int chunkSize) =>
        (fileSize + chunkSize - 1) / chunkSize * chunkSize;

    private static long EnsureInitialFileSize(string filePath, long initialSize)
    {
        using var stream = new FileStream(filePath, FileMode.OpenOrCreate);
        if (stream.Length < initialSize)
            stream.SetLength(initialSize);

        return stream.Length;
    }

    private void CreateMappingsForSize(long size)
    {
        var currentChunks = (long)mappings.Count;
        var neededChunks = (size + chunkSize - 1) / chunkSize;

        for (var chunk = currentChunks; chunk < neededChunks; chunk++)
        {
            var mapName = $"{fileName}_chunk_{chunk}";
            var offset = chunk * chunkSize;

            var file = MemoryMappedFile.CreateFromFile(
                filePath,
                FileMode.Open,
                mapName,
                0,
                MemoryMappedFileAccess.ReadWrite);

            var view = file.CreateViewAccessor(
                offset,
                chunkSize,
                MemoryMappedFileAccess.ReadWrite);

            mappings.Add(file);
            views.Add(view);
        }
    }

    public void Grow()
    {
        ThrowIfDisposed();
        CreateMappingsForSize(SetFileSize(filePath, fileSize * 2));
    }

    private static long SetFileSize(string filePath, long newSize)
    {
        using var stream = new FileStream(filePath, FileMode.Open);
        stream.SetLength(newSize);
        return stream.Length;
    }

    public readonly ref struct Lease
    {
        public Lease(
            Span<byte> bytes,
            SafeMemoryMappedViewHandle handle)
        {
            Bytes = bytes;
            this.handle = handle;
        }

        public readonly Span<byte> Bytes;
        private readonly SafeMemoryMappedViewHandle handle;
        public void Release() => handle.ReleasePointer();
    }

    public unsafe Lease Read(long offset, int length)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var chunkIndex = (int)offset / chunkSize;
        var chunkOffset = (int)offset % chunkSize;

        if (chunkIndex >= views.Count || chunkOffset + length > chunkSize)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset or length exceeds mapped region.");

        byte* ptr = null;
        var handle = views[chunkIndex].SafeMemoryMappedViewHandle;
        handle.AcquirePointer(ref ptr);

        return new Lease(new Span<byte>(ptr + chunkOffset, length), handle);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var view in views)
            view.Dispose();

        foreach (var mapping in mappings)
            mapping.Dispose();

        views.Clear();
        mappings.Clear();

        disposed = true;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
