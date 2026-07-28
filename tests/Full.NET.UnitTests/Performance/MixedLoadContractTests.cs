using System.Net;
using System.Diagnostics.Metrics;
using System.Text;
using Full.NET.Benchmarks.MixedLoad;

namespace Full.NET.UnitTests.Performance;

[TestClass]
public sealed class MixedLoadContractTests
{
    [TestMethod]
    public void Defaults_freeze_formal_matrix_and_error_budget()
    {
        var options = MixedLoadOptions.Parse([]);

        CollectionAssert.AreEqual(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 4, 16, 32 },
            options.ConcurrencyLevels.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Warmup);
        Assert.AreEqual(TimeSpan.FromMinutes(10), options.Duration);
        Assert.AreEqual(20260728, options.Seed);
        Assert.AreEqual(0.005d, options.MaximumUnexpectedErrorRate);
    }

    [TestMethod]
    public void Manifest_covers_authentication_reads_writes_audit_and_outbox()
    {
        var scenarios = MixedLoadScenarioCatalog.Default;

        Assert.AreEqual(100, scenarios.Sum(scenario => scenario.Weight));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Authentication == MixedLoadAuthentication.Jwt
            && scenario.Operation == MixedLoadOperation.Read));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Authentication == MixedLoadAuthentication.Jwt
            && scenario.Operation == MixedLoadOperation.Write));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Authentication == MixedLoadAuthentication.ApiKey
            && scenario.Operation == MixedLoadOperation.Read));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Authentication == MixedLoadAuthentication.ApiKey
            && scenario.Operation == MixedLoadOperation.Write));
        Assert.IsTrue(scenarios.Any(scenario => scenario.IsAuditQuery));
        Assert.IsTrue(scenarios.Any(scenario => scenario.ProducesOutbox));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.IsExpectedValidationFailure
            && scenario.ExpectedStatusCode == HttpStatusCode.BadRequest));
    }

    [TestMethod]
    public void Manifest_freezes_stable_names_and_weights()
    {
        var manifest = MixedLoadScenarioCatalog.Default
            .Select(scenario => $"{scenario.Name}:{scenario.Weight}")
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "jwt-read:25",
                "jwt-write-outbox:15",
                "api-key-read:25",
                "api-key-write-outbox:15",
                "audit-list:10",
                "validation-failure:10",
            },
            manifest);
    }

    [TestMethod]
    public void Selector_is_repeatable_for_the_same_seed()
    {
        var first = new MixedLoadScenarioSelector(
            MixedLoadScenarioCatalog.Default,
            20260728);
        var second = new MixedLoadScenarioSelector(
            MixedLoadScenarioCatalog.Default,
            20260728);

        var firstSequence = Enumerable.Range(0, 100)
            .Select(_ => first.Next().Name)
            .ToArray();
        var secondSequence = Enumerable.Range(0, 100)
            .Select(_ => second.Next().Name)
            .ToArray();

        CollectionAssert.AreEqual(firstSequence, secondSequence);
    }

    [TestMethod]
    public void Parser_supports_short_correctness_runs_and_rejects_invalid_shapes()
    {
        var options = MixedLoadOptions.Parse(
        [
            "--providers", "mysql",
            "--concurrency", "1,4",
            "--warmup-seconds", "1",
            "--duration-seconds", "5",
            "--seed", "17",
            "--max-error-rate", "0.01",
            "--output", "artifacts/mixed-load",
        ]);

        CollectionAssert.AreEqual(new[] { "mysql" }, options.Providers.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 4 }, options.ConcurrencyLevels.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.Warmup);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.Duration);
        Assert.AreEqual(17, options.Seed);
        Assert.AreEqual(0.01d, options.MaximumUnexpectedErrorRate);
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(["--providers", "postgres"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(["--concurrency", "4,4"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MixedLoadOptions.Parse(["--duration-seconds", "0"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(["--unknown", "value"]));
    }

    [TestMethod]
    public void Required_metrics_cover_tail_latency_database_and_backlog()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "client.request.duration",
                "client.response.status",
                "fullnet.dapper.statement",
                "db.client.connection.pool",
                "process.cpu",
                "process.gc",
                "database.container.cpu",
                "database.container.memory",
                "database.lock_wait",
                "fullnet.audit.write",
                "fullnet.outbox.backlog",
            },
            MixedLoadMetricContract.Required.ToArray());
    }

    [TestMethod]
    public void Statistics_use_nearest_rank_tail_percentiles()
    {
        var statistics = MixedLoadLatencyStatistics.Calculate(
            Enumerable.Range(1, 100).Select(value => (double)value).ToArray());

        Assert.AreEqual(50d, statistics.P50Milliseconds);
        Assert.AreEqual(95d, statistics.P95Milliseconds);
        Assert.AreEqual(99d, statistics.P99Milliseconds);
    }

    [TestMethod]
    public void Provider_budgets_are_explicit_and_keep_the_error_contract()
    {
        var sqlServer = MixedLoadProviderBudget.Create("sqlserver", 0.005d);
        var mySql = MixedLoadProviderBudget.Create("mysql", 0.005d);

        Assert.AreEqual(750d, sqlServer.P95Milliseconds);
        Assert.AreEqual(2500d, sqlServer.P99Milliseconds);
        Assert.AreEqual(1000d, mySql.P95Milliseconds);
        Assert.AreEqual(3000d, mySql.P99Milliseconds);
        Assert.AreEqual(0.005d, sqlServer.MaximumUnexpectedErrorRate);
        Assert.AreEqual(0.005d, mySql.MaximumUnexpectedErrorRate);
        Assert.AreEqual(85d, sqlServer.MaximumHostProcessCpuPercent);
        Assert.AreEqual(85d, sqlServer.MaximumDatabaseContainerCpuPercent);
    }

    [TestMethod]
    public async Task Checkpoint_persists_raw_samples_before_releasing_them()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-mixed-load-checkpoint-{Guid.NewGuid():N}");
        try
        {
            var options = MixedLoadOptions.Parse(
            [
                "--providers", "sqlserver",
                "--concurrency", "1",
                "--warmup-seconds", "0",
                "--duration-seconds", "1",
                "--output", outputDirectory,
            ]);
            var capturedAt = DateTimeOffset.UtcNow;
            var processBefore = new MixedLoadProcessSnapshot(
                capturedAt,
                100,
                1000,
                500,
                1,
                0,
                0);
            var processAfter = new MixedLoadProcessSnapshot(
                capturedAt.AddSeconds(1),
                200,
                2000,
                600,
                2,
                0,
                0);
            var databaseBefore = new MixedLoadDatabaseSnapshot(
                capturedAt,
                0,
                0,
                null,
                1,
                0,
                0,
                0,
                null);
            var databaseAfter = new MixedLoadDatabaseSnapshot(
                capturedAt.AddSeconds(1),
                2,
                0,
                null,
                1,
                0,
                0,
                0,
                null);
            var samples = new[]
            {
                new MixedLoadRequestSample(
                    0,
                    0,
                    "jwt-read",
                    capturedAt,
                    10,
                    200,
                    200,
                    null),
                new MixedLoadRequestSample(
                    1,
                    0,
                    "jwt-read",
                    capturedAt.AddMilliseconds(10),
                    12,
                    200,
                    200,
                    null),
            };
            var run = MixedLoadReportWriter.CreateRunResult(
                "sqlserver",
                "sqlserver:test",
                "test",
                1,
                options,
                TimeSpan.FromSeconds(1),
                samples,
                new MixedLoadDapperSnapshot(
                    new Dictionary<string, long>(),
                    null,
                    0),
                CreateCompletePoolSnapshot(),
                CreateCompleteContainerSnapshot(),
                processBefore,
                processAfter,
                databaseBefore,
                databaseAfter);

            var checkpoint = await MixedLoadReportWriter
                .WriteRunCheckpointAsync(
                    outputDirectory,
                    run,
                    CancellationToken.None);

            Assert.HasCount(0, checkpoint.Samples);
            var rawPath = Path.Combine(
                outputDirectory,
                "raw",
                "sqlserver-c1-samples.ndjson");
            Assert.IsTrue(File.Exists(rawPath));
            Assert.HasCount(2, await File.ReadAllLinesAsync(rawPath));
            Assert.HasCount(
                0,
                Directory.GetFiles(
                    Path.GetDirectoryName(rawPath)!,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Response_consumer_waits_until_the_entire_body_is_buffered()
    {
        var content = new ProbeHttpContent("""{"value":"ok"}""");
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        };
        var scenario = MixedLoadScenarioCatalog.Default.Single(item =>
            item.Name == "jwt-read");

        var tenant = await MixedLoadResponseConsumer.ConsumeAsync(
            response,
            scenario,
            CancellationToken.None);

        Assert.IsNull(tenant);
        Assert.AreEqual(1, content.SerializationCount);
    }

    [TestMethod]
    public void MySql_pool_telemetry_filters_pool_and_captures_required_metrics()
    {
        var poolName = $"mixed-load-test-{Guid.NewGuid():N}";
        using var telemetry = new MySqlConnectionPoolTelemetry(poolName);
        using var meter = new Meter("MySqlConnector");
        var usage = meter.CreateUpDownCounter<long>(
            "db.client.connections.usage");
        var pending = meter.CreateUpDownCounter<long>(
            "db.client.connections.pending_requests");
        var timeouts = meter.CreateCounter<long>(
            "db.client.connections.timeouts");
        var wait = meter.CreateHistogram<double>(
            "db.client.connections.wait_time");
        var poolTag = new KeyValuePair<string, object?>("pool.name", poolName);
        _ = meter.CreateObservableUpDownCounter(
            "db.client.connections.max",
            () => new Measurement<long>(100, poolTag));

        telemetry.Reset();
        usage.Add(
            3,
            poolTag,
            new KeyValuePair<string, object?>("state", "used"));
        usage.Add(
            5,
            poolTag,
            new KeyValuePair<string, object?>("state", "idle"));
        pending.Add(2, poolTag);
        timeouts.Add(1, poolTag);
        wait.Record(0.012d, poolTag);
        usage.Add(
            99,
            new KeyValuePair<string, object?>("pool.name", "other"),
            new KeyValuePair<string, object?>("state", "used"));

        var snapshot = telemetry.Snapshot();
        var repeatedSnapshot = telemetry.Snapshot();

        Assert.IsTrue(snapshot.EvidenceComplete, snapshot.EvidenceError);
        Assert.AreEqual(3d, snapshot.PeakActiveConnections);
        Assert.AreEqual(5d, snapshot.PeakIdleConnections);
        Assert.AreEqual(8d, snapshot.PeakPooledConnections);
        Assert.AreEqual(2d, snapshot.PeakPendingRequests);
        Assert.AreEqual(1L, snapshot.ConnectionTimeouts);
        Assert.AreEqual(12d, snapshot.WaitDuration!.P95Milliseconds, 0.001d);
        Assert.AreEqual(100, snapshot.ConfiguredMaximumConnections);
        Assert.AreEqual(100, repeatedSnapshot.ConfiguredMaximumConnections);
        Assert.IsTrue(snapshot.CapacityHeadroomPassed);
        Assert.AreEqual(90, snapshot.MaximumSafeActiveConnections);
    }

    [TestMethod]
    public void Evidence_gap_fails_the_formal_run_gate()
    {
        var options = MixedLoadOptions.Parse(
        [
            "--providers", "sqlserver",
            "--concurrency", "1",
            "--warmup-seconds", "0",
            "--duration-seconds", "1",
        ]);
        var capturedAt = DateTimeOffset.UtcNow;
        var sample = new MixedLoadRequestSample(
            0,
            0,
            "jwt-read",
            capturedAt,
            10,
            200,
            200,
            null);
        var processBefore = new MixedLoadProcessSnapshot(
            capturedAt,
            0,
            0,
            0,
            0,
            0,
            0);
        var processAfter = processBefore with
        {
            CapturedAtUtc = capturedAt.AddSeconds(1),
        };
        var database = new MixedLoadDatabaseSnapshot(
            capturedAt,
            0,
            0,
            null,
            1,
            0,
            0,
            0,
            null);
        var run = MixedLoadReportWriter.CreateRunResult(
            "sqlserver",
            "sqlserver:test",
            "test",
            1,
            options,
            TimeSpan.FromSeconds(1),
            [sample],
            new MixedLoadDapperSnapshot(
                new Dictionary<string, long>
                {
                    ["test"] = 1,
                },
                MixedLoadLatencyStatistics.Calculate([1d]),
                1),
            CreateCompletePoolSnapshot(),
            CreateCompleteContainerSnapshot(),
            processBefore,
            processAfter,
            database,
            database with
            {
                CapturedAtUtc = capturedAt.AddSeconds(1),
            });
        var report = MixedLoadReportWriter.CreateReport(
            options,
            [
                new MixedLoadProviderResult(
                    "sqlserver",
                    run.ContainerImage,
                    run.DatabaseVersion,
                    [run]),
            ]);

        Assert.IsFalse(run.Evidence.Passed);
        Assert.IsTrue(run.Evidence.FailureReasons.Any(reason =>
            reason.Contains("Dapper", StringComparison.Ordinal)));
        Assert.IsFalse(run.BudgetEvaluation.Passed);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => MixedLoadReportWriter.EnsurePassed(report));
    }

    [TestMethod]
    public void Container_cpu_is_normalized_to_the_host_capacity()
    {
        var cpuPercent = MixedLoadContainerTelemetry.CalculateCpuPercentOfHost(
            currentContainerUsage: 150,
            previousContainerUsage: 100,
            currentSystemUsage: 2000,
            previousSystemUsage: 1000);

        Assert.AreEqual(5d, cpuPercent, 0.001d);
        Assert.AreEqual(
            0d,
            MixedLoadContainerTelemetry.CalculateCpuPercentOfHost(
                100,
                100,
                1000,
                1000));
    }

    private static MixedLoadConnectionPoolSnapshot CreateCompletePoolSnapshot() =>
        new(
            100,
            1,
            1,
            2,
            0,
            0,
            0,
            0,
            null,
            ["test"],
            90,
            true,
            "test",
            true,
            null);

    private static MixedLoadContainerSnapshot CreateCompleteContainerSnapshot() =>
        new(
            1,
            5,
            5,
            1024,
            true,
            null);

    private sealed class ProbeHttpContent(string value) : HttpContent
    {
        private readonly byte[] _content = Encoding.UTF8.GetBytes(value);

        public int SerializationCount { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            SerializationCount++;
            return stream.WriteAsync(_content).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _content.Length;
            return true;
        }
    }
}
