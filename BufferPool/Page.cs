namespace BufferPool;

public sealed class Page(byte[] buffer)
    : IDisposable
{
    private readonly byte[] buffer = buffer;
    private readonly ReaderWriterLockSlim gate = new();

    public int Size { get; } = buffer.Length;

    public WriteLock TryGetWriteLock(TimeSpan timeout) => gate.TryEnterWriteLock(timeout)
            ? new WriteLock(gate, buffer)
            : default;

    public ReadLock TryGetReadLock(TimeSpan timeout) => gate.TryEnterWriteLock(timeout)
            ? new ReadLock(gate, buffer)
            : default;

    public void Dispose() => gate.Dispose();
}
