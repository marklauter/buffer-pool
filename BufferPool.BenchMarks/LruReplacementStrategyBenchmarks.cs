using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", 
    Justification = "disposable is disposed in Cleanup method")]
public sealed class LruReplacementStrategyBenchmarks 
{
    private LruReplacementStrategy<int> strategy = default!;
    private readonly CancellationToken cancellationToken = CancellationToken.None;

    [Params(100, 1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        strategy = new();

        // Pre-populate the strategy with N items
        for (var i = 0; i < N; i++)
        {
            strategy.BumpAsync(i, cancellationToken).AsTask().Wait();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        strategy.Dispose();
    }

    [Benchmark]
    public async Task BumpFirstItemAsync()
    {
        await strategy.BumpAsync(0, cancellationToken);
    }

    [Benchmark]
    public async Task BumpLastItemAsync()
    {
        await strategy.BumpAsync(N - 1, cancellationToken);
    }

    [Benchmark]
    public async Task BumpRandomItemAsync()
    {
        var random = Random.Shared.Next(0, N);
        await strategy.BumpAsync(random, cancellationToken);
    }

    [Benchmark]
    public async Task EvictLastItemAsync()
    {
        await strategy.TryEvictAsync(cancellationToken);
    }

}
