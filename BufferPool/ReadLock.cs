namespace BufferPool;

public readonly ref struct ReadLock
{
    private readonly ReaderWriterLockSlim gate;

    internal ReadLock(
        ReaderWriterLockSlim gate,
        ReadOnlySpan<byte> buffer)
    {
        this.gate = gate;
        Buffer = buffer;
        Aquired = true;
    }

    public readonly ReadOnlySpan<byte> Buffer;
    public readonly bool Aquired = false;

    public void Unlock() => gate.ExitWriteLock();
}
