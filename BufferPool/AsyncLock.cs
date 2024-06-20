namespace BufferPool;

internal sealed class AsyncLock
    : IDisposable
{
    private readonly struct LockScope
        : IDisposable
    {
        private readonly AsyncLock asyncLock;

        internal LockScope(AsyncLock toRelease)
        {
            asyncLock = toRelease;
        }

        public void Dispose() => _ = asyncLock.latch.Release();
    }

    private bool disposed;
    private readonly SemaphoreSlim latch = new(1, 1);

    public async ValueTask<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        await ThrowIfDisposed().latch.WaitAsync(cancellationToken);
        return new LockScope(this);
    }

    public async ValueTask<TReturn> WithLockAsync<TReturn>(Func<CancellationToken, ValueTask<TReturn>> func, CancellationToken cancellationToken)
    {
        using var scope = await LockAsync(cancellationToken);
        return await func(cancellationToken);
    }

    public async ValueTask<TReturn> WithLockAsync<TReturn>(Func<TReturn> func, CancellationToken cancellationToken)
    {
        using var scope = await LockAsync(cancellationToken);
        return func();
    }

    public async ValueTask WithLockAsync(Action action, CancellationToken cancellationToken)
    {
        using var scope = await LockAsync(cancellationToken);
        action();
    }
    
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        latch.Dispose();

        disposed = true;
    }

    private AsyncLock ThrowIfDisposed() => disposed
        ? throw new ObjectDisposedException(nameof(AsyncLock))
        : this;
}
