namespace BufferPool;

internal sealed class AsyncLock
    : IDisposable
{
    private bool disposed;
    private readonly SemaphoreSlim latch = new(1, 1);

    public async ValueTask<TReturn> WithLockAsync<TReturn>(Func<CancellationToken, ValueTask<TReturn>> func, CancellationToken cancellationToken)
    {
        await ThrowIfDisposed().latch.WaitAsync(cancellationToken);
        try
        {
            return await func(cancellationToken);
        }
        finally
        {
            _ = latch.Release();
        }
    }

    public async ValueTask<TReturn> WithLockAsync<TReturn>(Func<TReturn> func, CancellationToken cancellationToken)
    {
        await ThrowIfDisposed().latch.WaitAsync(cancellationToken);
        try
        {
            return func();
        }
        finally
        {
            _ = latch.Release();
        }
    }

    public async ValueTask WithLockAsync(Action action, CancellationToken cancellationToken)
    {
        await ThrowIfDisposed().latch.WaitAsync(cancellationToken);
        try
        {
            action();
        }
        finally
        {
            _ = latch.Release();
        }
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
