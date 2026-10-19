using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Full.NET.Hosting.Observability;
using Serilog;
using Serilog.Events;

namespace Full.NET.Benchmarks.Logging;

/// <summary>正式日志管道的有限准入特征矩阵；调用耗时不代表 HTTP 请求或下游持久投递耗时。</summary>
public static class LoggingAdmissionMatrixRunner
{
    private const int Producers = 4;
    private const int EventsPerProducer = 4096;
    private static readonly string[] Scenarios = ["count", "bytes", "flood", "oversize", "disabled", "mixed"];

    /// <summary>输出仅含有限统计，不记录业务正文，不允许覆盖已有文件或在仓库内积累产物。</summary>
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("用法：logging-admission-matrix --output <新的系统临时目录>");
        var output = Path.GetFullPath(args[1]);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("输出必须是尚不存在的系统临时子目录。");
        Directory.CreateDirectory(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await RunCaseAsync("flood", 32, 0, deadline.Token);
        var cases = new List<object>();
        foreach (var round in new[] { 1, 2 })
        foreach (var scenario in round == 1 ? Scenarios : Scenarios.Reverse())
        foreach (var payload in round == 1 ? new[] { 32, 2048 } : new[] { 2048, 32 })
        {
            cases.Add(await RunCaseAsync(scenario, payload, round, deadline.Token));
            await File.WriteAllTextAsync(Path.Combine(output, "cases.json"),
                JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
            Console.WriteLine($"Logging admission round={round} scenario={scenario} payload={payload} reconciled");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed = true, completedAtUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, producers = Producers, eventsPerProducer = EventsPerProducer,
            rounds = 2, cases,
            scope = "Production Serilog configuration, safe snapshots and bounded dual workers; callbacks consume memory only. "
                + "Call latency includes Serilog/enrichment/admission, not HTTP or disk/Kafka/ES. CPU/allocation include finite driver/sampling. "
                + "Blocked general output is deliberately released after measurement; no end-to-end durability or unmeasured capacity claim.",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }

    private static async Task<object> RunCaseAsync(string scenario, int payloadCharacters, int round, CancellationToken token)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var monitors = new FullNetLoggingMonitors();
        var oversize = new long[2];
        var byteDropped = new long[2];
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, own) =>
        {
            if (instrument.Meter.Name == FullNetAsyncLogMonitor.MeterName
                && instrument.Name is "fullnet.logging.events.oversize" or "fullnet.logging.events.byte_budget_dropped")
                own.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key != "channel") continue;
                var channel = Equals(tag.Value, FullNetAsyncLogMonitor.HighPriorityChannel) ? 1 : 0;
                if (instrument.Name == "fullnet.logging.events.oversize") oversize[channel] = value;
                else byteDropped[channel] = value;
            }
        });
        listener.Start();
        var blocked = scenario is "count" or "bytes" or "mixed";
        long generalConsumed = 0, priorityConsumed = 0;
        var options = new LoggingOptions
        {
            AsyncBufferSize = scenario is "count" or "mixed" ? 1 : 1024,
            HighPriorityAsyncBufferSize = 1024,
            MaxEventBytes = 2048,
            GeneralQueueMaxBytes = scenario == "bytes" ? 4352 : 1024 * 1024,
            HighPriorityQueueMaxBytes = 1024 * 1024,
        };
        // 走正式生产配置而非重写路由；只把下游换成可控且不递归产生日志的内存出口。
        var configuration = FullNetLoggingPipeline.Configure(new LoggerConfiguration(), "LoggingAdmissionProbe", options, monitors,
            _ =>
            {
                if (blocked)
                {
                    entered.Set();
                    release.Wait();
                }
                Interlocked.Increment(ref generalConsumed);
            },
            _ => Interlocked.Increment(ref priorityConsumed));
        var logger = configuration.CreateLogger();
        var source = scenario == "oversize" ? logger.ForContext("SourceContext", new string('s', 2048)) : logger;
        var payload = new string('p', payloadCharacters);
        long generalOffered = 0;
        long priorityOffered = 0;
        var latencies = Enumerable.Range(0, Producers).Select(_ => new double[EventsPerProducer]).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] producers = [];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var disposed = false;
        try
        {
            if (blocked)
            {
                logger.Information("Admission warmup");
                generalOffered++;
                if (!entered.Wait(TimeSpan.FromSeconds(10), token)) throw new InvalidOperationException("普通出口未阻塞。");
                // 字节场景填到首次字节拒绝；条数场景只需占满一个待消费名额。
                do
                {
                    logger.Information("Admission queued");
                    generalOffered++;
                } while (scenario == "bytes" && monitors.General.Snapshot.DroppedMessagesCount == 0 && generalOffered < 1024);
                if (scenario == "bytes"
                    ? monitors.General.ByteUsage.ReservedBytes == 0 || monitors.General.Snapshot.Count >= options.AsyncBufferSize
                        || monitors.General.Snapshot.DroppedMessagesCount != 1
                    : monitors.General.Snapshot.Count != 1 || monitors.General.Snapshot.DroppedMessagesCount != 0)
                    throw new InvalidOperationException("未建立所需的独立条数/字节饱和前置。");
            }
            var generalDropsBefore = monitors.General.Snapshot.DroppedMessagesCount;
            double oldestGeneral = 0, oldestPriority = 0;
            long generalHighWaterBytes = 0, priorityHighWaterBytes = 0;
            var sampleGate = new object();
            void Sample()
            {
                lock (sampleGate)
                {
                    oldestGeneral = Math.Max(oldestGeneral, monitors.General.OldestPendingSeconds);
                    oldestPriority = Math.Max(oldestPriority, monitors.HighPriority.OldestPendingSeconds);
                    generalHighWaterBytes = Math.Max(generalHighWaterBytes, monitors.General.ByteUsage.ReservedBytes);
                    priorityHighWaterBytes = Math.Max(priorityHighWaterBytes, monitors.HighPriority.ByteUsage.ReservedBytes);
                }
            }
            producers = Enumerable.Range(0, Producers).Select(producer => Task.Run(async () =>
            {
                await start.Task.WaitAsync(stop.Token);
                for (var index = 0; index < EventsPerProducer; index++)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    var level = scenario == "disabled" ? LogEventLevel.Debug
                        : scenario == "mixed" && index % 64 == 0 ? LogEventLevel.Error : LogEventLevel.Information;
                    var before = Stopwatch.GetTimestamp();
                    source.Write(level, "Admission {Payload}", payload);
                    latencies[producer][index] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
                    if (level >= LogEventLevel.Error) Interlocked.Increment(ref priorityOffered);
                    else if (level >= LogEventLevel.Information) Interlocked.Increment(ref generalOffered);
                    if (index % 64 == 0) Sample();
                }
            }, stop.Token)).ToArray();
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            var allocatedStart = GC.GetTotalAllocatedBytes();
            var watch = Stopwatch.StartNew();
            start.SetResult();
            await Task.WhenAll(producers);
            watch.Stop();
            var cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            var allocatedBytes = GC.GetTotalAllocatedBytes() - allocatedStart;
            Sample();
            var beforeDrain = new
            {
                generalDepth = monitors.General.Snapshot.Count, priorityDepth = monitors.HighPriority.Snapshot.Count,
                generalReservedBytes = monitors.General.ByteUsage.ReservedBytes,
                priorityReservedBytes = monitors.HighPriority.ByteUsage.ReservedBytes,
            };
            var measuredGeneralDrops = monitors.General.Snapshot.DroppedMessagesCount - generalDropsBefore;
            listener.RecordObservableInstruments();
            release.Set();
            logger.Dispose();
            disposed = true;
            var generalDrops = monitors.General.Snapshot.DroppedMessagesCount;
            var priorityDrops = monitors.HighPriority.Snapshot.DroppedMessagesCount;
            var expectedMeasuredGeneral = Producers * EventsPerProducer - priorityOffered;
            if (generalConsumed + generalDrops != generalOffered || priorityConsumed + priorityDrops != priorityOffered
                || monitors.General.WorkerState != 3 || monitors.HighPriority.WorkerState != 3
                || monitors.General.ByteUsage.ReservedBytes != 0 || monitors.HighPriority.ByteUsage.ReservedBytes != 0
                || generalHighWaterBytes > options.GeneralQueueMaxBytes || priorityHighWaterBytes > options.HighPriorityQueueMaxBytes
                || (scenario is "count" or "bytes" or "mixed" or "oversize" && measuredGeneralDrops != expectedMeasuredGeneral)
                || (scenario == "disabled" && (generalOffered != 0 || priorityOffered != 0))
                || (scenario == "mixed" && (priorityConsumed != Producers * (EventsPerProducer / 64) || priorityDrops != 0))
                || (scenario == "bytes" && byteDropped[0] != generalDrops)
                || (scenario == "oversize" && oversize[0] != generalDrops)
                || (scenario is "count" or "mixed" && (oversize[0] != 0 || byteDropped[0] != 0))
                || (scenario == "flood" && generalConsumed == 0))
                throw new InvalidOperationException("准入矩阵的消费、丢弃、预约或线程退出对账失败。");
            return new
            {
                round, scenario, payloadCharacters, elapsedMs = watch.Elapsed.TotalMilliseconds, cpuMs, allocatedBytes,
                attempts = Producers * EventsPerProducer, generalOffered, priorityOffered,
                generalConsumed, priorityConsumed, generalDrops, priorityDrops, measuredGeneralDrops,
                generalOversize = oversize[0], priorityOversize = oversize[1],
                generalByteDropped = byteDropped[0], priorityByteDropped = byteDropped[1],
                generalCapacity = options.AsyncBufferSize, generalByteCapacity = options.GeneralQueueMaxBytes,
                priorityCapacity = options.HighPriorityAsyncBufferSize, priorityByteCapacity = options.HighPriorityQueueMaxBytes,
                beforeDrain, sampledGeneralHighWaterBytes = generalHighWaterBytes, sampledPriorityHighWaterBytes = priorityHighWaterBytes,
                oldestGeneralSeconds = oldestGeneral, oldestPrioritySeconds = oldestPriority,
                callMs = Percentiles(latencies.SelectMany(values => values)), workersStopped = true,
                generalCallMs = scenario == "disabled" ? null
                    : Percentiles(latencies.SelectMany(values => values.Where((_, index) => scenario != "mixed" || index % 64 != 0))),
                priorityCallMs = scenario == "mixed"
                    ? Percentiles(latencies.SelectMany(values => values.Where((_, index) => index % 64 == 0))) : null,
            };
        }
        finally
        {
            await stop.CancelAsync();
            start.TrySetCanceled();
            release.Set();
            try { await Task.WhenAll(producers); }
            finally { if (!disposed) logger.Dispose(); }
        }
    }

    private static object Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double At(double quantile) => sorted[(int)Math.Ceiling(sorted.Length * quantile) - 1];
        return new { count = sorted.Length, p50 = At(.5), p95 = At(.95), p99 = At(.99), max = sorted[^1] };
    }
}
