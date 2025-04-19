using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BufferPool.ReplacementStrategies;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "disposable is disposed in Cleanup method")]
public class LruReplacementStrategyBumpBestCase
{
    private readonly CancellationToken cancellationToken = CancellationToken.None;
    private DefaultLruReplacementStrategy<int> defaultStrategy = default!;
    private OptimizedLruReplacementStrategy<int> optimizedStrategy = default!;
    private int first;

    [Params(1000, 10000, 100000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        defaultStrategy = new();
        optimizedStrategy = new();
        first = N - 1;

        // Pre-populate the strategy with N items
        for (var i = 0; i < N; i++)
        {
            defaultStrategy.BumpAsync(i, cancellationToken).AsTask().Wait();
            optimizedStrategy.BumpAsync(i, cancellationToken).AsTask().Wait();
        }
    }

    [GlobalCleanup]
    public void Cleanup() =>
        defaultStrategy.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask DefaultBumpFirstAsync() =>
        defaultStrategy.BumpAsync(first, cancellationToken);

    [Benchmark]
    public ValueTask OptimizedBumpFirstAsync() =>
        optimizedStrategy.BumpAsync(first, cancellationToken);
}
