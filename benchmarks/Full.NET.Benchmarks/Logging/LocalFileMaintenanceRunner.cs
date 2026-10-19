using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Full.NET.Hosting.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace Full.NET.Benchmarks.Logging;

/// <summary>真实双消费线程与 Local 文件出口的有限特征测试；不等同于业务请求或整机磁盘容量。</summary>
public static class LocalFileMaintenanceRunner
{
    private const int GeneralEvents = 4096;
    private const int PriorityEvents = 100;
    private static readonly MessageTemplate Template = new MessageTemplateParser().Parse("Local probe {Payload}");

    /// <summary>只接受新建的系统临时输出目录；逐场景回收文件，仅保留有界统计与运行环境。</summary>
    public static async Task RunAsync(string[] args)
    {
        if (args.Length is < 2 or > 3 || args[0] != "--output"
            || args.Length == 3 && args[2] != "--quota-comparison")
            throw new ArgumentException("用法：logging-local-maintenance --output <新的系统临时目录> [--quota-comparison]");
        var quotaComparison = args.Length == 3;
        var measuredGeneralEvents = quotaComparison ? GeneralEvents * 4 : GeneralEvents;
        var output = Path.GetFullPath(args[1]);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("输出必须是尚不存在的系统临时子目录。");
        Directory.CreateDirectory(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var cases = new List<object>();
        // 先预热相同线程、快照和文件路径，随后正反序各一轮；不复用上一场景残留文件。
        await RunCaseAsync(output, 16, "steady", 0, deadline.Token);
        if (quotaComparison)
        {
            // 唯一变量为优先保留比例；正反序重复，不能把保留收益或调度波动归成全面吞吐改善。
            foreach (var round in new[] { 1, 2, 3, 4 })
            foreach (var reserve in round % 2 == 1 ? new[] { 0, 20 } : new[] { 20, 0 })
            {
                cases.Add(await RunCaseAsync(output, 9900, "quota", round, deadline.Token, reserve, measuredGeneralEvents));
                await SaveCasesAsync();
                Console.WriteLine($"Local quota comparison round={round} reserve={reserve} reconciled");
            }
        }
        else
        {
            foreach (var round in new[] { 1, 2 })
            {
                var sizes = round == 1 ? new[] { 16, 1000, 9900 } : new[] { 9900, 1000, 16 };
                foreach (var count in sizes)
                foreach (var phase in new[] { "steady", "minute", "expiry", "quota" })
                {
                    var result = await RunCaseAsync(output, count, phase, round, deadline.Token);
                    cases.Add(result);
                    // 每个完整场景即保存收据，失败时仍能定位已完成的范围。
                    await SaveCasesAsync();
                    Console.WriteLine($"Local maintenance round={round} files={count} phase={phase} reconciled");
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed = true, completedAtUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, generalEvents = measuredGeneralEvents, priorityEvents = PriorityEvents,
            payloadCharacters = 128, rounds = quotaComparison ? 4 : 2, quotaComparison, cases,
            scope = "Real FullNetBoundedAsyncSink workers and LocalFileLogStore; virtual UTC triggers maintenance; "
                + "producer and queue-to-file latency include snapshot/scheduling; maintenance call includes directory lock wait; "
                + "pure lock wait is not measured separately; no HTTP/Kafka/ES, fsync durability or unmeasured capacity claim",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);

        Task SaveCasesAsync() => File.WriteAllTextAsync(Path.Combine(output, "cases.json"),
            JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }

    private static async Task<object> RunCaseAsync(string output, int fileCount, string phase, int round, CancellationToken token,
        int priorityReservePercent = 20, int generalEvents = GeneralEvents)
    {
        var root = Path.Combine(output, $"case-{round}-{fileCount}-{phase}-{priorityReservePercent}");
        var directory = Path.Combine(root, "Api");
        Directory.CreateDirectory(directory);
        var seedBytes = Encoding.UTF8.GetBytes("{\"seed\":\"" + new string('x', 244) + "\"}\n");
        for (var index = 0; index < fileCount; index++)
        {
            token.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(Path.Combine(directory, $"fullnet-general-20261010-{Guid.NewGuid():N}.txt"), seedBytes, token);
        }
        var options = new LocalFileLoggingOptions
        {
            Directory = root, FileSizeLimitBytes = phase == "quota" ? 4096 : 65536,
            MaxDirectoryBytes = seedBytes.Length * (long)fileCount + (phase == "quota" ? 8192 : 8 * 1024 * 1024),
            PriorityReservePercent = priorityReservePercent,
        };
        var clock = new LocalClock();
        var generalLatency = new List<double>(generalEvents);
        var priorityLatency = new List<double>(PriorityEvents);
        var priorityIds = Enumerable.Range(0, PriorityEvents).Select(_ => Guid.NewGuid().ToString("D")).ToHashSet(StringComparer.Ordinal);
        var generalCall = new double[generalEvents];
        var priorityCall = new double[PriorityEvents];
        long observedBytes = -1;
        long removed = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, own) =>
        {
            if (instrument.Meter.Name == FullNetAsyncLogMonitor.MeterName
                && instrument.Name is "fullnet.logging.local_file.bytes" or "fullnet.logging.local_file.files_removed")
                own.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "fullnet.logging.local_file.bytes") Interlocked.Exchange(ref observedBytes, value);
            else Interlocked.Add(ref removed, value);
        });
        listener.Start();
        var store = new LocalFileLogStore(options, root, "Api", clock);
        using var generalMonitor = new FullNetAsyncLogMonitor();
        using var priorityMonitor = new FullNetAsyncLogMonitor(FullNetAsyncLogMonitor.HighPriorityChannel);
        var general = new FullNetBoundedAsyncSink(null, 256, "local-probe-general", generalMonitor, 2048, 1024 * 1024,
            emitSnapshot: envelope => generalLatency.Add(Stopwatch.GetElapsedTime(envelope.QueuedAtTimestamp).TotalMilliseconds), localFile: store.General);
        var priority = new FullNetBoundedAsyncSink(null, 256, "local-probe-priority", priorityMonitor, 2048, 1024 * 1024,
            emitSnapshot: envelope => priorityLatency.Add(Stopwatch.GetElapsedTime(envelope.QueuedAtTimestamp).TotalMilliseconds), localFile: store.Priority);
        var success = false;
        var stopped = false;
        var watch = Stopwatch.StartNew();
        using var loadStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task[] producers = [];
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var allocatedStart = GC.GetTotalAllocatedBytes();
        double oldestGeneral = 0, oldestPriority = 0, maintenanceMs = 0;
        long highWaterBytes = 0;
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var payload = new ScalarValue(new string('p', 128));
            var generalTask = ProduceAsync(false);
            var priorityTask = ProduceAsync(true);
            producers = [generalTask, priorityTask];
            if (phase == "minute") clock.Advance(TimeSpan.FromSeconds(61));
            if (phase == "expiry") clock.Advance(TimeSpan.FromDays(15));
            start.SetResult();
            var maintenance = Stopwatch.StartNew();
            store.Maintain();
            maintenanceMs = maintenance.Elapsed.TotalMilliseconds;
            await Task.WhenAll(generalTask, priorityTask);
            while (general.ReservedBytes != 0 || priority.ReservedBytes != 0)
            {
                Sample();
                await Task.Delay(2, token);
            }
            Sample();
            general.Complete(); priority.Complete();
            while (!general.WaitForCompletion(TimeSpan.Zero) || !priority.WaitForCompletion(TimeSpan.Zero))
            {
                Sample();
                await Task.Delay(2, token);
            }
            stopped = true;
            watch.Stop();
            var allocated = GC.GetTotalAllocatedBytes() - allocatedStart;
            var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            var files = Directory.GetFiles(directory, "fullnet-*.txt");
            var diskBytes = files.Sum(path => new FileInfo(path).Length);
            if (diskBytes != observedBytes || diskBytes > options.MaxDirectoryBytes || files.Length > 10000
                || general.ReservedBytes != 0 || priority.ReservedBytes != 0
                || generalMonitor.WorkerState != 3 || priorityMonitor.WorkerState != 3
                || general.OversizeCount != 0 || priority.OversizeCount != 0
                || generalLatency.Count == 0 || priority.DroppedMessagesCount != 0
                || generalLatency.Count + general.DroppedMessagesCount != generalEvents
                || priorityLatency.Count + priority.DroppedMessagesCount != PriorityEvents)
                throw new InvalidOperationException("Local 队列、字节或文件上限对账失败。");
            var retainedPriority = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in files.Where(path => Path.GetFileName(path).StartsWith("fullnet-priority-", StringComparison.Ordinal)))
            foreach (var line in File.ReadLines(path))
            {
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("LogEventId", out var id) && id.GetString() is { } value && priorityIds.Contains(value))
                    retainedPriority.Add(value);
            }
            // 在计时结束后核对普通文件，区分未接收的丢弃和消费后因保留策略删除，种子行没有事件 ID。
            var retainedGeneral = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in files.Where(path => Path.GetFileName(path).StartsWith("fullnet-general-", StringComparison.Ordinal)))
            foreach (var line in File.ReadLines(path))
            {
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("LogEventId", out var id) && id.GetString() is { } value
                    && Guid.TryParseExact(value, "D", out _) && !priorityIds.Contains(value))
                    retainedGeneral.Add(value);
            }
            if (retainedGeneral.Count > generalLatency.Count || phase != "quota" && retainedGeneral.Count != generalLatency.Count)
                throw new InvalidOperationException("普通事件文件收据与实际消费数不一致。");
            // 持续配额对照必须覆盖新事件之间的空间竞争，不能只删除旧种子而误报保留权衡已验证。
            if (phase == "quota" && generalEvents > GeneralEvents && retainedGeneral.Count == generalLatency.Count)
                throw new InvalidOperationException("持续配额场景未触发普通新事件保留删除，负载不足以验收保留权衡。");
            if (phase != "quota" && retainedPriority.Count + priority.DroppedMessagesCount != PriorityEvents)
                throw new InvalidOperationException("非配额场景优先事件未完成文件收据对账。");
            success = true;
            return new
            {
                round, fileCount, phase, priorityReservePercent, elapsedMs = watch.Elapsed.TotalMilliseconds, cpuMs = cpu, allocatedBytes = allocated,
                seedFileBytes = seedBytes.Length, fileSizeLimitBytes = options.FileSizeLimitBytes,
                directoryByteBudget = options.MaxDirectoryBytes,
                maintenanceCallMs = maintenanceMs, pureDirectoryLockWaitMs = (double?)null,
                generalOffered = generalEvents, priorityOffered = PriorityEvents,
                generalConsumed = generalLatency.Count, priorityConsumed = priorityLatency.Count,
                generalDrops = general.DroppedMessagesCount, priorityDrops = priority.DroppedMessagesCount,
                generalEventsPerSecond = generalLatency.Count / watch.Elapsed.TotalSeconds,
                priorityEventsPerSecond = priorityLatency.Count / watch.Elapsed.TotalSeconds,
                generalCallMs = Percentiles(generalCall), priorityCallMs = Percentiles(priorityCall),
                generalQueueToFileMs = Percentiles(generalLatency), priorityQueueToFileMs = Percentiles(priorityLatency),
                oldestGeneralSeconds = oldestGeneral, oldestPrioritySeconds = oldestPriority,
                filesRemoved = removed, managedFiles = files.Length, finalDiskBytes = diskBytes,
                lastObservedLogicalBytes = observedBytes, highWaterLogicalBytes = highWaterBytes,
                retainedPriority = retainedPriority.Count, priorityRemovedByRetention = PriorityEvents - priority.DroppedMessagesCount - retainedPriority.Count,
                retainedGeneral = retainedGeneral.Count, generalRemovedByRetention = generalEvents - general.DroppedMessagesCount - retainedGeneral.Count,
                generalReservedBytes = general.ReservedBytes, priorityReservedBytes = priority.ReservedBytes,
            };

            async Task ProduceAsync(bool highPriority)
            {
                await start.Task.WaitAsync(loadStop.Token);
                var latencies = highPriority ? priorityCall : generalCall;
                var sink = highPriority ? priority : general;
                var ids = highPriority ? priorityIds.ToArray() : [];
                for (var index = 0; index < latencies.Length; index++)
                {
                    loadStop.Token.ThrowIfCancellationRequested();
                    var entry = new LogEvent(clock.GetUtcNow(), highPriority ? LogEventLevel.Error : LogEventLevel.Information,
                        null, Template, [new LogEventProperty("Payload", payload),
                            new LogEventProperty("LogEventId", new ScalarValue(highPriority ? ids[index] : Guid.NewGuid().ToString("D")))]);
                    var before = Stopwatch.GetTimestamp();
                    sink.Emit(entry);
                    latencies[index] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
                    if (highPriority) await Task.Delay(2, loadStop.Token);
                    else if (index % 16 == 15) await Task.Delay(1, loadStop.Token);
                    Sample();
                }
            }

            void Sample()
            {
                // 两个有限生产循环的采样串行化只保护基准统计，不包裹生产出口。
                lock (listener)
                {
                    oldestGeneral = Math.Max(oldestGeneral, general.OldestPendingSeconds);
                    oldestPriority = Math.Max(oldestPriority, priority.OldestPendingSeconds);
                    listener.RecordObservableInstruments();
                    highWaterBytes = Math.Max(highWaterBytes, Interlocked.Read(ref observedBytes));
                }
            }
        }
        finally
        {
            try
            {
                await loadStop.CancelAsync();
                try { await Task.WhenAll(producers); }
                catch (OperationCanceledException) when (loadStop.IsCancellationRequested) { }
            }
            finally
            {
                general.Complete(); priority.Complete();
                if (!stopped)
                {
                    var closedGeneral = general.WaitForCompletion(TimeSpan.FromSeconds(10));
                    var closedPriority = priority.WaitForCompletion(TimeSpan.FromSeconds(10));
                    if (!closedGeneral || !closedPriority)
                        throw new IOException("Local 基准后台未停止；保留现场，不删除活动文件。");
                }
                // 失败保留当前场景最小现场；成功时两个后台线程已关闭租约，才删除精确自有目录。
                if (success) Directory.Delete(root, true);
            }
        }
    }

    private static object Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double At(double quantile) => sorted.Length == 0 ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * quantile) - 1)];
        return new { count = sorted.Length, p50 = At(.5), p95 = At(.95), p99 = At(.99), max = sorted.Length == 0 ? 0 : sorted[^1] };
    }

    /// <summary>仅推进 UTC 与刷新时间以触发真实到期路径，队列年龄仍使用真实单调时钟。</summary>
    private sealed class LocalClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan value) => Interlocked.Add(ref _ticks, value.Ticks);
    }
}
