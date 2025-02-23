using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BufferPool.Benchmarks;

var config = DefaultConfig.Instance
    .AddJob(Job
        .MediumRun
        .WithPlatform(Platform.AnyCpu)
        .WithRuntime(CoreRuntime.Core80)
        .WithToolchain(InProcessEmitToolchain.Instance));
// .WithLaunchCount(1)
// .WithToolchain(InProcessNoEmitToolchain.Instance));

var bumpSummary = BenchmarkRunner.Run<LruReplacementStrategyBumpBenchmarks>(config);
var evictionSummary = BenchmarkRunner.Run<LruReplacementStrategyEvictionBenchmarks>(config);

Console.WriteLine(bumpSummary.Title);
Console.WriteLine(evictionSummary.Title);
