using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BufferPool.Benchmarks;

var config = DefaultConfig.Instance
    .AddJob(Job
        .ShortRun
        .WithPlatform(Platform.AnyCpu)
        .WithRuntime(CoreRuntime.Core80)
        .WithToolchain(InProcessEmitToolchain.Instance));

_ = BenchmarkRunner.Run<LruReplacementStrategyBumpWorstCase>(config);
//_ = BenchmarkRunner.Run<LruReplacementStrategyBumpBestCase>(config);
//_ = BenchmarkRunner.Run<LruReplacementStrategyTryEvict>(config);
