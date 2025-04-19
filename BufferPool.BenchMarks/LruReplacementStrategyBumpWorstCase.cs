using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BufferPool.ReplacementStrategies;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace BufferPool.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "disposable is disposed in Cleanup method")]
public class LruReplacementStrategyBumpWorstCase
{
    private readonly CancellationToken cancellationToken = CancellationToken.None;
    private DefaultLruReplacementStrategy<int> defaultStrategy = default!;
    private OptimizedLruReplacementStrategy<int> optimizedStrategy = default!;
    private LruReplacementStrategy<int> syncStrategy = default!;
    private int lastD = -1;
    private int lastO = -1;
    private int lastS = -1;

    [Params(1000, 10000, 100000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        defaultStrategy = new();
        optimizedStrategy = new();
        syncStrategy = new();

        // Pre-populate the strategy with N items
        for (var i = 0; i < N; i++)
        {
            defaultStrategy.BumpAsync(i, cancellationToken).AsTask().Wait();
            optimizedStrategy.BumpAsync(i, cancellationToken).AsTask().Wait();
            syncStrategy.Touch(i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetLast(ref int last)
    {
        last = (last + 1) % N;
        return last;
    }

    [GlobalCleanup]
    public void Cleanup() =>
        defaultStrategy.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask DefaultBumpLastAsync() =>
        defaultStrategy.BumpAsync(GetLast(ref lastD), cancellationToken);

    [Benchmark]
    public ValueTask OptimizedBumpLastAsync() =>
        optimizedStrategy.BumpAsync(GetLast(ref lastO), cancellationToken);

    [Benchmark]
    public void SyncBumpLast() =>
        syncStrategy.Touch(GetLast(ref lastS));
}
