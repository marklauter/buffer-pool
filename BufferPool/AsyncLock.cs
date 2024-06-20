namespace BufferPool;

internal sealed class AsyncLock
    : IDisposable
{
    private readonly SemaphoreSlim latch = new(1, 1);
    private bool disposed;

    public async Task EnterAsync(Action action, CancellationToken cancellationToken)
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

    public async ValueTask EnterAsync(Func<CancellationToken, ValueTask> func, CancellationToken cancellationToken)
    {
        await ThrowIfDisposed().latch.WaitAsync(cancellationToken);
        try
        {
            await func(cancellationToken);
        }
        finally
        {
            _ = latch.Release();
        }
    }

    public async ValueTask<T> EnterAsync<T>(Func<CancellationToken, ValueTask<T>> func, CancellationToken cancellationToken)
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
