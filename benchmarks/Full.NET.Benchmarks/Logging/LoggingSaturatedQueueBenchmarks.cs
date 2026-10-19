using System.Text;
using BenchmarkDotNet.Attributes;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Benchmarks.Logging;

/// <summary>固定阻塞后台出口，测量条数满而字节预算未满时调用线程的拒绝成本；不是端到端容量测试。</summary>
[MemoryDiagnoser]
public class LoggingSaturatedQueueBenchmarks
{
    private const int EventsPerInvocation = 16384;
    private IHost _host = null!;
    private ILogger<LoggingSaturatedQueueBenchmarks> _logger = null!;
    private FullNetLoggingMonitors _monitors = null!;
    private readonly BlockedWriter _writer = new();
    private string _payload = null!;
    private long _expectedDrops;

    [Params(32, 2048)]
    public int PayloadCharacters { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = new string('x', PayloadCharacters);
        var original = Console.Out;
        Console.SetOut(_writer);
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration["FullNet:Logging:DeliveryMode"] = "Collector";
            builder.Configuration["FullNet:Logging:ExpectedDeliveryMode"] = "Collector";
            builder.Configuration["FullNet:Logging:AsyncBufferSize"] = "1";
            builder.Configuration["FullNet:Logging:GeneralQueueMaxBytes"] = "1048576";
            builder.AddFullNetServiceDefaults();
            _host = builder.Build();
            _logger = _host.Services.GetRequiredService<ILogger<LoggingSaturatedQueueBenchmarks>>();
            _monitors = _host.Services.GetRequiredService<FullNetLoggingMonitors>();
            _logger.LogInformation("Saturation warmup {Payload}", _payload);
            if (!_writer.Entered.Wait(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("后台出口未进入阻塞状态，基准无效。");
            _logger.LogInformation("Saturation queued {Payload}", _payload);
            if (_monitors.General.Snapshot.Count != 1)
                throw new InvalidOperationException("条数队列未满，基准无效。");
            _expectedDrops = _monitors.General.Snapshot.DroppedMessagesCount;
        }
        catch
        {
            _writer.Release.Set();
            _host?.Dispose();
            throw;
        }
        finally
        {
            // 首条后台写入已捕获阻塞 Writer；恢复控制台，避免阻塞基准框架自身的结果输出。
            Console.SetOut(original);
        }
    }

    [Benchmark(OperationsPerInvoke = EventsPerInvocation)]
    public void RejectWhenCountFull()
    {
        for (var index = 0; index < EventsPerInvocation; index++)
            _logger.LogInformation("Saturation rejected {Payload}", _payload);
        _expectedDrops += EventsPerInvocation;
    }

    [IterationCleanup]
    public void ValidateRejections()
    {
        var snapshot = _monitors.General.Snapshot;
        if (snapshot.Count != 1 || snapshot.DroppedMessagesCount != _expectedDrops)
            throw new InvalidOperationException("队列或丢弃计数偏离固定饱和场景，样本无效。");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _writer.Release.Set();
        _host?.Dispose();
        _writer.Dispose();
    }

    /// <summary>只阻塞已进入的后台输出；释放后丢弃内容，避免把输出 I/O 计入调用线程样本。</summary>
    private sealed class BlockedWriter : TextWriter
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value)
        {
            Entered.Set();
            Release.Wait();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Entered.Dispose();
                Release.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
