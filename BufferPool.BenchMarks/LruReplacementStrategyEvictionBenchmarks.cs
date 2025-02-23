using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "disposable is disposed in Cleanup method")]
public sealed class LruReplacementStrategyEvictionBenchmarks
{
    private DefaultLruReplacementStrategy<int> strategy = default!;
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
    public void Cleanup() =>
        strategy.Dispose();

    [Benchmark]
    public async Task EvictSpecificItemAsync() =>
        // Evict a specific item
        _ = await strategy.TryEvictAsync(N / 2, cancellationToken);

    [Benchmark]
    public async Task EvictLastItemAsync() =>
        // Evict a oldest item
        _ = await strategy.TryEvictAsync(cancellationToken);
}

