using BenchmarkDotNet.Attributes;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Benchmarks.Logging;

/// <summary>
/// 通过公开的宿主注册入口测量日志调用线程的构造、快照和入队成本。
/// 后台输出写入空 TextWriter；本基准不代表文件、采集器或 Kafka 的端到端容量。
/// </summary>
[MemoryDiagnoser]
public class LoggingHotPathBenchmarks
{
    private const int EventsPerInvocation = 32;
    private IHost _host = null!;
    private ILogger<LoggingHotPathBenchmarks> _logger = null!;
    private FullNetLoggingMonitors _monitors = null!;
    private TextWriter _originalOutput = null!;
    private long _generalDrops;
    private long _priorityDrops;

    [GlobalSetup]
    public void Setup()
    {
        _originalOutput = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration["FullNet:Logging:AsyncBufferSize"] = "262144";
            builder.Configuration["FullNet:Logging:HighPriorityAsyncBufferSize"] = "262144";
            builder.Configuration["FullNet:Logging:GeneralQueueMaxBytes"] = "1073741824";
            builder.Configuration["FullNet:Logging:HighPriorityQueueMaxBytes"] = "1073741824";
            builder.AddFullNetServiceDefaults();
            _host = builder.Build();
            _logger = _host.Services.GetRequiredService<ILogger<LoggingHotPathBenchmarks>>();
            _monitors = _host.Services.GetRequiredService<FullNetLoggingMonitors>();
            _generalDrops = _monitors.General.Snapshot.DroppedMessagesCount;
            _priorityDrops = _monitors.HighPriority.Snapshot.DroppedMessagesCount;
        }
        catch
        {
            Console.SetOut(_originalOutput);
            throw;
        }
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvocation)]
    public void DisabledLevel()
    {
        for (var index = 0; index < EventsPerInvocation; index++)
        {
            _logger.LogDebug("Log benchmark request {RequestId} completed in {ElapsedMs} ms", index, 12);
        }
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvocation)]
    public void GeneralSummary()
    {
        for (var index = 0; index < EventsPerInvocation; index++)
        {
            _logger.LogInformation("Log benchmark request {RequestId} completed in {ElapsedMs} ms", index, 12);
        }
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvocation)]
    public void HighPrioritySummary()
    {
        for (var index = 0; index < EventsPerInvocation; index++)
        {
            _logger.LogError("Log benchmark request {RequestId} failed with {StatusCode}", index, 503);
        }
    }

    [IterationCleanup]
    public void CheckDrops()
    {
        if (!SpinWait.SpinUntil(
                () => _monitors.General.Snapshot.Count == 0
                    && _monitors.HighPriority.Snapshot.Count == 0,
                TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException(
                "Logging benchmark did not drain its queue; this sample is invalid.");
        }

        var generalDrops = _monitors.General.Snapshot.DroppedMessagesCount;
        var priorityDrops = _monitors.HighPriority.Snapshot.DroppedMessagesCount;
        if (generalDrops != _generalDrops || priorityDrops != _priorityDrops)
        {
            throw new InvalidOperationException(
                "Logging benchmark saturated its queue; this sample is invalid.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            _host?.Dispose();
        }
        finally
        {
            Console.SetOut(_originalOutput);
        }
    }
}
