namespace BufferPool;

// todo: need to add reference count so we know when it's save to evict
// ^ actually, the read lock and write lock belong in the page buffer because that's where the access list is updated
// ^ try read lock and try write lock would replace the indexer in page buffer
public sealed class Page(byte[] buffer, int id)
    : IEquatable<Page>
    , IDisposable
{
    internal readonly byte[] buffer = buffer;
    private readonly ReaderWriterLockSlim gate = new();

    public int Size { get; } = buffer.Length;
    public int Id { get; } = id;

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

    public WriteLock TryGetWriteLock(TimeSpan timeout) => gate.TryEnterWriteLock(timeout)
            ? new WriteLock(gate, buffer)
            : default;

    public ReadLock TryGetReadLock(TimeSpan timeout) => gate.TryEnterWriteLock(timeout)
            ? new ReadLock(gate, buffer)
            : default;

    public void Dispose() => gate.Dispose();

    public override int GetHashCode() => Id;

    public override bool Equals(object? obj) => Equals((Page?)obj);

    public bool Equals(Page? other) => other is not null && Id == other.Id;

    public static bool operator ==(Page left, Page right) => left is null
        ? throw new ArgumentNullException(nameof(left))
        : left.Equals(right);

    public static bool operator !=(Page left, Page right) => left is null
        ? throw new ArgumentNullException(nameof(left))
        : !left.Equals(right);
}
