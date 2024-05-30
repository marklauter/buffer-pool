namespace BufferPool;

public readonly ref struct WriteLock
{
    private readonly ReaderWriterLockSlim gate;

    internal WriteLock(
        ReaderWriterLockSlim gate,
        Span<byte> buffer)
    {
        this.gate = gate;
        Buffer = buffer;
        Aquired = true;
    }

    public readonly Span<byte> Buffer;
    public readonly bool Aquired = false;

    public void Unlock() => gate.ExitWriteLock();
}
