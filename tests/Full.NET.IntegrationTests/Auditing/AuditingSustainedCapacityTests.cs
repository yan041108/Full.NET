extern alias kafkabenchmarks;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Extensions.DependencyInjection;
using Latency = kafkabenchmarks::Full.NET.Benchmarks.MixedLoad.MixedLoadLatencyStatistics;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>B1 正式协调器的双库持续写入，等待与落库都核验；不替代 HTTP 业务容量。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AuditingSustainedCapacityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Sustained_coordinator_writes_every_operation_and_exception(DatabaseProvider provider)
    {
        Assert.IsTrue(SharedDatabaseFixture.BoundedContainers && !SharedDatabaseFixture.ReuseContainers,
            "容量验收必须由独享有界资源控制器启动。");
        var directory = Directory.CreateTempSubdirectory("fullnet-b1-capacity-" + provider + "-").FullName;
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var token = deadline.Token;
        await using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync(token);
        var coordinator = factory.Services.GetRequiredService<AuditMicroBatchCoordinator>();
        var reports = new List<object>();
        foreach (var (concurrency, round) in new[] { (1, 1), (8, 1), (32, 1), (32, 2) })
        {
            var report = await RunCaseAsync(factory, coordinator, directory, concurrency, round, token);
            reports.Add(report);
            await File.WriteAllTextAsync(Path.Combine(directory, "cases.json"), JsonSerializer.Serialize(reports), token);
            Console.Error.WriteLine($"B1 capacity {provider} concurrency={concurrency} round={round} reconciled");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
        {
            passed = true, provider = provider.ToString(), reports, completedAtUtc = DateTimeOffset.UtcNow,
            durationSecondsPerCase = 30, processorCount = Environment.ProcessorCount,
            scope = "Real Host B1 coordinator, Dapper and transactions; closed-loop 1/8/32 plus 32 repeat; "
                + "one operation and ten-percent exception, exact DB content and counters reconciled; "
                + "caller completion is not durability acknowledgement unless matched by DB evidence; no HTTP P99 or 10K/Soak claim",
        }), token);
    }

    private static async Task<object> RunCaseAsync(FullNetApiFactory factory, AuditMicroBatchCoordinator coordinator,
        string directory, int concurrency, int round, CancellationToken token)
    {
        // 固定少量预热先证明正式写入路径可用，预热使用独立 TraceId，不污染实测计数。
        var warmupTrace = Guid.NewGuid().ToString("N");
        await Task.WhenAll(Enumerable.Range(1, 64).Select(sequence => coordinator.FlushImportantAsync(
            Operation(sequence, warmupTrace), null, token)));
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var warmup = await ReadOperationsAsync(queries, warmupTrace, token);
        Assert.AreEqual(64, warmup.Count);
        Assert.AreEqual(0L, coordinator.QueueBytesInUse);

        var trace = Guid.NewGuid().ToString("N");
        using var metrics = new Observation();
        var latencies = new ConcurrentQueue<double>();
        var next = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long timestamp = 0;
        DateTimeOffset startedAtUtc = default;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime.TotalMilliseconds;
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sampler = SampleAsync();
        var workers = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await start.Task;
            while (Stopwatch.GetElapsedTime(timestamp) < TimeSpan.FromSeconds(30))
            {
                token.ThrowIfCancellationRequested();
                Assert.IsFalse(metrics.HasFailure, "B1 已失败或拒绝，停止追加压力。");
                var sequence = Interlocked.Increment(ref next);
                Assert.IsTrue(sequence <= 100000, "超过已声明的单档十万调用收据上限。");
                var operation = Operation(sequence, trace);
                var exception = sequence % 10 == 0 ? new ExceptionLogWriteModel(
                    "FullNET.CapacityProbeException", "capacity-exception-" + sequence.ToString(CultureInfo.InvariantCulture),
                    "capacity-stack", "POST", "/capacity/b1", null, null, trace, null) : null;
                var began = Stopwatch.GetTimestamp();
                await coordinator.FlushImportantAsync(operation, exception, token);
                latencies.Enqueue(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            }
        }).ToArray();
        timestamp = Stopwatch.GetTimestamp();
        startedAtUtc = DateTimeOffset.UtcNow;
        start.SetResult();
        Exception? failure = null;
        try { await Task.WhenAll(workers); }
        catch (Exception exception) { failure = exception; }
        finally
        {
            await sampling.CancelAsync();
            try { await sampler; } catch (OperationCanceledException) when (sampling.IsCancellationRequested) { }
            metrics.Sample();
        }
        var elapsed = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        process.Refresh();
        var cpu = process.TotalProcessorTime.TotalMilliseconds - cpuBefore;
        var samples = latencies.ToArray();
        var observed = metrics.Snapshot();
        var raw = new
        {
            concurrency, round, trace, measurementStartUtc = startedAtUtc, elapsedMilliseconds = elapsed, attempted = next, completed = samples.Length,
            latencyMilliseconds = samples, telemetry = observed, failure = failure?.GetType().Name,
            sharedProcessCpuMilliseconds = cpu, allocatedBytes = GC.GetTotalAllocatedBytes() - allocatedBefore,
            observedProcessLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
        };
        // 即使写库/预算核验失败也先保存原始等待时序，不能只保留有利的已完成样本。
        await File.WriteAllTextAsync(Path.Combine(directory, $"{concurrency}-{round}-measurement.json"), JsonSerializer.Serialize(raw), token);
        Assert.IsNull(failure, "持续写入未完整完成，查看 measurement.json。");
        Assert.IsTrue(samples.Length > 0 && elapsed is >= 30000 and < 45000);
        var rows = await ReadOperationsAsync(queries, trace, token);
        var exceptions = await queries.QueryAsync<string>(new SqlStatement("test.b1_capacity.exceptions",
            "SELECT Message FROM fn_auditing_exception_log WHERE TraceId = @TraceId", SqlDataScope.Global), new { TraceId = trace }, token);
        Assert.AreEqual(samples.Length, rows.Count);
        Assert.AreEqual(samples.Length, rows.Select(row => row.ActionKey).Distinct(StringComparer.Ordinal).Count());
        var expectedKeys = Enumerable.Range(1, samples.Length).Select(sequence => "capacity." + sequence.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(expectedKeys.SetEquals(rows.Select(row => row.ActionKey)));
        foreach (var row in rows)
        {
            var sequence = int.Parse(row.ActionKey["capacity.".Length..], CultureInfo.InvariantCulture);
            Assert.AreEqual(sequence % 10 == 0 ? 500 : 200, row.StatusCode);
        }
        var expectedExceptions = Enumerable.Range(1, samples.Length).Where(sequence => sequence % 10 == 0)
            .Select(sequence => "capacity-exception-" + sequence.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        Assert.AreEqual(expectedExceptions.Count, exceptions.Count);
        Assert.IsTrue(expectedExceptions.SetEquals(exceptions));
        Assert.AreEqual((double)samples.Length, metrics.Value("accepted", "kind=operation"));
        Assert.AreEqual((double)expectedExceptions.Count, metrics.Value("accepted", "kind=exception"));
        Assert.IsFalse(metrics.HasFailure);
        Assert.AreEqual((double)(samples.Length + expectedExceptions.Count), metrics.Value("batch_rows"));
        Assert.AreEqual(0L, coordinator.QueueBytesInUse);
        Assert.AreEqual(0d, metrics.Gauge("bytes"));
        Assert.AreEqual(0d, metrics.Gauge("depth"));
        Assert.IsTrue(metrics.Maximum("oldest_age") < 2 && metrics.Maximum("bytes") <= 67108864
            && metrics.Maximum("depth") <= 4096);
        var statistics = Latency.Calculate(samples);
        var wait = metrics.WaitMilliseconds.ToArray();
        Assert.AreEqual(samples.Length + expectedExceptions.Count, wait.Length);
        Assert.IsTrue(statistics.P99Milliseconds < 500);
        Assert.IsTrue(cpu / elapsed / 2 * 100 <= 95 && process.PeakWorkingSet64 < 1L << 30);
        return new
        {
            concurrency, round, statistics, measurementStartUtc = startedAtUtc, latencyMilliseconds = samples, elapsedMilliseconds = elapsed,
            callsPerSecond = samples.Length * 1000d / elapsed,
            rowsPerSecond = (rows.Count + exceptions.Count) * 1000d / elapsed,
            chargedEnvelopeBytesPerSecond = metrics.Value("batch_bytes") * 1000d / elapsed,
            operationRows = rows.Count, exceptionRows = exceptions.Count, uniqueOperations = expectedKeys.Count,
            unexpected = 0, queueBytesAfter = coordinator.QueueBytesInUse,
            waitStatistics = Latency.Calculate(wait), waitMilliseconds = wait, telemetry = observed,
            cpuPercentOfTwoCores = cpu / elapsed / 2 * 100, observedProcessLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
            raw,
        };

        async Task SampleAsync()
        {
            while (true) { metrics.Sample(); await Task.Delay(20, sampling.Token); }
        }
    }

    private static OperationLogWriteModel Operation(int sequence, string trace) => new(
        "capacity." + sequence.ToString(CultureInfo.InvariantCulture), "POST", "/capacity/b1",
        sequence % 10 == 0 ? 500 : 200, 1, sequence % 10 != 0, null, null, trace, null, "capacity.probe");

    private static Task<IReadOnlyList<OperationRow>> ReadOperationsAsync(IQueryExecutor queries, string trace, CancellationToken token) =>
        queries.QueryAsync<OperationRow>(new SqlStatement("test.b1_capacity.operations",
            "SELECT ActionKey, StatusCode FROM fn_auditing_operation_log WHERE TraceId = @TraceId", SqlDataScope.Global), new { TraceId = trace }, token);

    private sealed record OperationRow(string ActionKey, int StatusCode);

    /// <summary>只记录本批真实 Meter；旁路回调不抛异常，统一在验收线程断言。</summary>
    private sealed class Observation : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double> _maximum = new(StringComparer.Ordinal);
        public ConcurrentQueue<double> WaitMilliseconds { get; } = new();
        private int _invalid;

        public Observation()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            { if (instrument.Meter.Name == AuditMicroBatchTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var labels = new List<string>();
            foreach (var tag in tags)
            {
                if (tag.Key is not ("kind" or "reason" or "outcome")) Interlocked.Exchange(ref _invalid, 1);
                labels.Add(tag.Key + "=" + tag.Value);
            }
            var name = instrument.Name["fullnet.auditing.microbatch.".Length..];
            if (name == "wait")
            {
                if (WaitMilliseconds.Count < 200000) WaitMilliseconds.Enqueue(value * 1000);
                else Interlocked.Exchange(ref _invalid, 1);
            }
            else if (name.StartsWith("queue.", StringComparison.Ordinal))
            {
                _values[name] = value;
                _maximum.AddOrUpdate(name[6..], value, (_, old) => Math.Max(old, value));
            }
            else _values.AddOrUpdate(name + ":" + string.Join(',', labels), value, (_, old) => old + value);
        }

        public bool HasFailure => Volatile.Read(ref _invalid) != 0 || _values.Any(pair =>
            pair.Value != 0 && (pair.Key.StartsWith("failed:", StringComparison.Ordinal)
                || pair.Key.StartsWith("rejected:", StringComparison.Ordinal) || pair.Key.StartsWith("poisoned:", StringComparison.Ordinal)));
        public double Value(string name, string labels = "") => _values.GetValueOrDefault(name + ":" + labels);
        public double Gauge(string name) => _values.TryGetValue("queue." + name, out var value) ? value : double.NaN;
        public double Maximum(string name) => _maximum.GetValueOrDefault(name, double.NaN);
        public void Sample() => _listener.RecordObservableInstruments();
        public object Snapshot() => new { values = _values.ToDictionary(pair => pair.Key, pair => pair.Value),
            maximum = _maximum.ToDictionary(pair => pair.Key, pair => pair.Value), invalid = _invalid };
        public void Dispose() => _listener.Dispose();
    }
}
