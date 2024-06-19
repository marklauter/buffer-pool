namespace BufferPool;

internal sealed class Latch
    : IDisposable
{
    private readonly SemaphoreSlim latch = new(1, 1);
    private bool disposed;

    public async Task CriticalSectionAsync(Action action, CancellationToken cancellationToken)
    {
        await latch.WaitAsync(cancellationToken);
        try
        {
            action();
        }
        finally
        {
            _ = latch.Release();
        }
    }

    public async ValueTask CriticalSectionAsync(Func<CancellationToken, ValueTask> func, CancellationToken cancellationToken)
    {
        await latch.WaitAsync(cancellationToken);
        try
        {
            await func(cancellationToken);
        }
        finally
        {
            _ = latch.Release();
        }
    }

    public async ValueTask<T> CriticalSectionAsync<T>(Func<CancellationToken, ValueTask<T>> func, CancellationToken cancellationToken)
    {
        await latch.WaitAsync(cancellationToken);
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
}
