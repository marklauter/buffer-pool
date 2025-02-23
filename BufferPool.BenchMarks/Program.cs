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

_ = BenchmarkRunner.Run<DefaultLruReplacementStrategyBumpBenchmarks>(config);
_ = BenchmarkRunner.Run<DefaultLruReplacementStrategyEvictionBenchmarks>(config);
_ = BenchmarkRunner.Run<OptimizedLruReplacementStrategyBumpBenchmarks>(config);
_ = BenchmarkRunner.Run<OptimizedLruReplacementStrategyEvictionBenchmarks>(config);
