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
        Assert.AreEqual(MixedLoadWorkload.Default, options.Workload);
        CollectionAssert.AreEqual(
            new[] { MixedLoadAuditWriteProfile.All },
            options.AuditWriteProfiles.ToArray());
    }

    [TestMethod]
    public void Manifest_covers_authentication_reads_writes_audit_and_direct_cache_invalidation()
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
        // Tenancy 写路径已改为提交后直接缓存失效，不得再通过 Outbox 冒充缓存同步。
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Operation == MixedLoadOperation.Write
            && scenario.Name.Contains("direct-cache-invalidation", StringComparison.Ordinal)
            && !scenario.ProducesOutbox));
        Assert.IsFalse(scenarios.Any(scenario => scenario.ProducesOutbox));
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
                "jwt-write-direct-cache-invalidation:15",
                "api-key-read:25",
                "api-key-write-direct-cache-invalidation:15",
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
    public void Outbox_retention_matrix_is_explicit_and_freezes_bounded_defaults()
    {
        var defaults = MixedLoadOptions.Parse([]);

        Assert.IsFalse(defaults.OutboxRetentionMatrixEnabled);
        Assert.HasCount(0, defaults.OutboxRetentionProfiles);
        Assert.AreEqual(10_000, defaults.OutboxRetentionSeedProcessed);
        Assert.AreEqual(200, defaults.OutboxRetentionBatchSize);
        Assert.AreEqual(15, defaults.OutboxRetentionMaxBatches);
        Assert.AreEqual(
            TimeSpan.Zero,
            defaults.OutboxRetentionInterval);

        var options = MixedLoadOptions.Parse(
        [
            "--outbox-retention-profiles", "off,on",
            "--outbox-retention-seed-processed", "500",
            "--outbox-retention-batch-size", "25",
            "--outbox-retention-max-batches", "5",
            "--outbox-retention-interval-ms", "100",
        ]);

        Assert.IsTrue(options.OutboxRetentionMatrixEnabled);
        CollectionAssert.AreEqual(
            new[]
            {
                MixedLoadOutboxRetentionProfile.Off,
                MixedLoadOutboxRetentionProfile.On,
            },
            options.OutboxRetentionProfiles.ToArray());
        Assert.AreEqual(500, options.OutboxRetentionSeedProcessed);
        Assert.AreEqual(25, options.OutboxRetentionBatchSize);
        Assert.AreEqual(5, options.OutboxRetentionMaxBatches);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(100),
            options.OutboxRetentionInterval);
    }

    [TestMethod]
    public void Outbox_retention_parser_rejects_ambiguous_or_unbounded_shapes()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-profiles", "on,on",
            ]));
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-profiles", "enabled",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-profiles", "off,on",
                "--outbox-retention-seed-processed", "0",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-profiles", "off,on",
                "--outbox-retention-batch-size", "1001",
            ]));
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-batch-size", "25",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => MixedLoadOptions.Parse(
            [
                "--outbox-retention-profiles", "off,on",
                "--outbox-retention-max-batches", "101",
            ]));
    }

    [TestMethod]
    public void Outbox_retention_comparison_gates_request_and_worker_tail_regression()
    {
        var off = CreateOutboxActivity(
            MixedLoadOutboxRetentionProfile.Off,
            requestP99Milliseconds: 100,
            workerP99Milliseconds: 40,
            deletedRows: 0);
        var passingOn = CreateOutboxActivity(
            MixedLoadOutboxRetentionProfile.On,
            requestP99Milliseconds: 115,
            workerP99Milliseconds: 45,
            deletedRows: 200);
        var failingOn = CreateOutboxActivity(
            MixedLoadOutboxRetentionProfile.On,
            requestP99Milliseconds: 121,
            workerP99Milliseconds: 40,
            deletedRows: 200);

        var passing = MixedLoadOutboxRetentionComparison.Evaluate(
            off,
            passingOn,
            maximumP99RegressionRatio: 0.20d);
        var failing = MixedLoadOutboxRetentionComparison.Evaluate(
            off,
            failingOn,
            maximumP99RegressionRatio: 0.20d);

        Assert.IsTrue(passing.Passed);
        Assert.AreEqual(0.15d, passing.RequestP99RegressionRatio, 0.001d);
        Assert.AreEqual(0.125d, passing.WorkerP99RegressionRatio, 0.001d);
        Assert.IsFalse(failing.Passed);
        Assert.IsFalse(failing.RequestP99Passed);

        var lowLatencyGuardBand = MixedLoadOutboxRetentionComparison.Evaluate(
            CreateOutboxActivity(
                MixedLoadOutboxRetentionProfile.Off,
                requestP99Milliseconds: 25,
                workerP99Milliseconds: 50,
                deletedRows: 0),
            CreateOutboxActivity(
                MixedLoadOutboxRetentionProfile.On,
                requestP99Milliseconds: 45,
                workerP99Milliseconds: 100,
                deletedRows: 200),
            maximumP99RegressionRatio: 0.20d);
        Assert.IsTrue(lowLatencyGuardBand.Passed);
    }

    [TestMethod]
    public void Parser_supports_audit_write_attribution_profiles()
    {
        var options = MixedLoadOptions.Parse(
        [
            "--workload", "audit-write",
            "--audit-write-profiles", "none,access,operation,exception,all",
        ]);

        Assert.AreEqual(MixedLoadWorkload.AuditWrite, options.Workload);
        CollectionAssert.AreEqual(
            new[]
            {
                MixedLoadAuditWriteProfile.None,
                MixedLoadAuditWriteProfile.Access,
                MixedLoadAuditWriteProfile.Operation,
                MixedLoadAuditWriteProfile.Exception,
                MixedLoadAuditWriteProfile.All,
            },
            options.AuditWriteProfiles.ToArray());
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(
            [
                "--workload", "default",
                "--audit-write-profiles", "none,all",
            ]));
        Assert.ThrowsExactly<ArgumentException>(
            () => MixedLoadOptions.Parse(
            [
                "--workload", "audit-write",
                "--audit-write-profiles", "all,all",
            ]));
    }

    [TestMethod]
    public void Audit_write_workload_exposes_one_two_and_three_write_paths()
    {
        var scenarios = MixedLoadScenarioCatalog.AuditWriteAttribution;

        Assert.AreEqual(100, scenarios.Sum(scenario => scenario.Weight));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.ExpectedAuditWrites == MixedLoadAuditWriteProfile.Access));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.ExpectedAuditWrites
                == (MixedLoadAuditWriteProfile.Access
                    | MixedLoadAuditWriteProfile.Operation)));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.ExpectedAuditWrites == MixedLoadAuditWriteProfile.All
            && scenario.Path == "/api/v1/auditing/exception-probes"
            && scenario.RequestMethod == HttpMethod.Post.Method
            && scenario.ExpectedStatusCode
                == HttpStatusCode.InternalServerError));
    }

    [TestMethod]
    public void Audit_write_policy_only_suppresses_unselected_audit_inserts()
    {
        Assert.IsFalse(MixedLoadAuditWritePolicy.ShouldExecute(
            MixedLoadAuditWriteProfile.None,
            "auditing.insert_access_log"));
        Assert.IsTrue(MixedLoadAuditWritePolicy.ShouldExecute(
            MixedLoadAuditWriteProfile.Access,
            "auditing.insert_access_log"));
        Assert.IsFalse(MixedLoadAuditWritePolicy.ShouldExecute(
            MixedLoadAuditWriteProfile.Access,
            "auditing.insert_operation_log"));
        Assert.IsTrue(MixedLoadAuditWritePolicy.ShouldExecute(
            MixedLoadAuditWriteProfile.All,
            "auditing.insert_exception_log"));
        Assert.IsTrue(MixedLoadAuditWritePolicy.ShouldExecute(
            MixedLoadAuditWriteProfile.None,
            "tenancy.update_tenant"));
    }

    [TestMethod]
    public void Audit_write_policy_expands_batch_statement_into_constituent_inserts()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "auditing.insert_access_log",
                "auditing.insert_operation_log",
                "auditing.insert_exception_log",
            },
            MixedLoadAuditWritePolicy.GetObservedStatements(
                "auditing.insert_request_audit_batch.access_operation_exception")
                .ToArray());
        CollectionAssert.AreEqual(
            new[] { "auditing.insert_operation_log" },
            MixedLoadAuditWritePolicy.GetObservedStatements(
                "auditing.insert_request_audit_batch.operation")
                .ToArray());
        Assert.AreEqual(
            0,
            MixedLoadAuditWritePolicy.GetObservedStatements(
                "tenancy.update_tenant").Count);
    }

    [TestMethod]
    public void Audit_write_profile_selector_balances_profiles_per_worker()
    {
        var selector = new MixedLoadAuditWriteProfileSelector(
        [
            MixedLoadAuditWriteProfile.None,
            MixedLoadAuditWriteProfile.Access,
            MixedLoadAuditWriteProfile.All,
        ],
            workerId: 1);

        var selected = Enumerable.Range(0, 6)
            .Select(sequence => selector.Select(sequence))
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                MixedLoadAuditWriteProfile.Access,
                MixedLoadAuditWriteProfile.All,
                MixedLoadAuditWriteProfile.None,
                MixedLoadAuditWriteProfile.Access,
                MixedLoadAuditWriteProfile.All,
                MixedLoadAuditWriteProfile.None,
            },
            selected);
    }

    [TestMethod]
    public void Audit_write_telemetry_attributes_attempts_and_tail_latency_by_profile()
    {
        var telemetry = new MixedLoadAuditWriteTelemetry();
        telemetry.Record(
            MixedLoadAuditWriteProfile.Access,
            "auditing.insert_access_log",
            4,
            succeeded: true);
        telemetry.Record(
            MixedLoadAuditWriteProfile.Access,
            "auditing.insert_access_log",
            9,
            succeeded: true);
        telemetry.Record(
            MixedLoadAuditWriteProfile.All,
            "auditing.insert_exception_log",
            12,
            succeeded: false);

        var snapshot = telemetry.Snapshot();
        var access = snapshot.Observations.Single(item =>
            item.Profile == MixedLoadAuditWriteProfile.Access
            && item.StatementName == "auditing.insert_access_log");
        var exception = snapshot.Observations.Single(item =>
            item.Profile == MixedLoadAuditWriteProfile.All
            && item.StatementName == "auditing.insert_exception_log");

        Assert.AreEqual(2L, access.Attempts);
        Assert.AreEqual(0L, access.Failures);
        Assert.AreEqual(9d, access.Duration!.P95Milliseconds);
        Assert.AreEqual(1L, exception.Attempts);
        Assert.AreEqual(1L, exception.Failures);
    }

    [TestMethod]
    public void Audit_write_attribution_compares_profile_latency_and_expected_inserts()
    {
        var scenario = MixedLoadScenarioCatalog.AuditWriteAttribution.Single(item =>
            item.Name == "audit-access-only");
        var capturedAt = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new MixedLoadRequestSample(
                0,
                0,
                scenario.Name,
                capturedAt,
                5,
                200,
                200,
                null,
                MixedLoadAuditWriteProfile.None),
            new MixedLoadRequestSample(
                1,
                0,
                scenario.Name,
                capturedAt,
                10,
                200,
                200,
                null,
                MixedLoadAuditWriteProfile.Access),
            new MixedLoadRequestSample(
                2,
                0,
                scenario.Name,
                capturedAt,
                20,
                200,
                200,
                null,
                MixedLoadAuditWriteProfile.All),
        };
        var telemetry = new MixedLoadAuditWriteTelemetry();
        telemetry.Record(
            MixedLoadAuditWriteProfile.Access,
            "auditing.insert_access_log",
            3,
            succeeded: true);
        telemetry.Record(
            MixedLoadAuditWriteProfile.All,
            "auditing.insert_access_log",
            4,
            succeeded: true);

        var result = MixedLoadAuditWriteAttribution.Create(
            samples,
            [scenario],
            telemetry.Snapshot(),
            [
                MixedLoadAuditWriteProfile.None,
                MixedLoadAuditWriteProfile.Access,
                MixedLoadAuditWriteProfile.All,
            ]);

        Assert.IsTrue(result.EvidenceComplete);
        Assert.AreEqual(5d, result.Profiles[0].Latency.P95Milliseconds);
        Assert.AreEqual(10d, result.Profiles[1].Latency.P95Milliseconds);
        Assert.AreEqual(20d, result.Profiles[2].Latency.P95Milliseconds);
        Assert.AreEqual(
            1L,
            result.Profiles[1].ExpectedStatementExecutions[
                "auditing.insert_access_log"]);
        Assert.AreEqual(
            1L,
            result.Profiles[1].ObservedStatementExecutions[
                "auditing.insert_access_log"]);
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
                new MixedLoadAuditWriteSnapshot([]),
                CreateCompletePoolSnapshot(),
                CreateCompleteContainerSnapshot(),
                processBefore,
                processAfter,
                databaseBefore,
                databaseAfter,
                new MixedLoadOutboxActivitySnapshot(
                    MixedLoadOutboxRetentionProfile.Off,
                    [
                        new MixedLoadOutboxOperationSample(
                            capturedAt,
                            "worker",
                            2,
                            1,
                            null),
                    ]));

            var checkpoint = await MixedLoadReportWriter
                .WriteRunCheckpointAsync(
                    outputDirectory,
                    run,
                    CancellationToken.None);

            Assert.HasCount(0, checkpoint.Samples);
            Assert.IsNotNull(checkpoint.OutboxActivity);
            Assert.HasCount(0, checkpoint.OutboxActivity.Samples);
            var rawPath = Path.Combine(
                outputDirectory,
                checkpoint.RawSampleFile.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(rawPath));
            Assert.HasCount(2, await File.ReadAllLinesAsync(rawPath));
            var outboxRawPath = Path.Combine(
                outputDirectory,
                checkpoint.OutboxActivity.RawSampleFile.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(outboxRawPath));
            Assert.HasCount(1, await File.ReadAllLinesAsync(outboxRawPath));
            await MixedLoadReportWriter.WriteAsync(
                outputDirectory,
                MixedLoadReportWriter.CreateReport(
                    options,
                    [
                        new MixedLoadProviderResult(
                            "sqlserver",
                            checkpoint.ContainerImage,
                            checkpoint.DatabaseVersion,
                            [checkpoint]),
                    ]),
                CancellationToken.None);
            Assert.IsTrue(File.Exists(
                Path.Combine(outputDirectory, "README.md")));
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
            new MixedLoadAuditWriteSnapshot([]),
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

    [TestMethod]
    public void MySql_innodb_status_parser_extracts_undo_history_length()
    {
        const string status =
            """
            TRANSACTIONS
            ------------
            Trx id counter 92841
            Purge done for trx's n:o < 92839 undo n:o < 0 state: running
            History list length 17
            """;

        Assert.AreEqual(
            17L,
            MixedLoadMySqlStatusParser.ParseHistoryListLength(status));
        Assert.IsNull(
            MixedLoadMySqlStatusParser.ParseHistoryListLength(
                "TRANSACTIONS without history evidence"));
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

    private static MixedLoadOutboxActivityResult CreateOutboxActivity(
        MixedLoadOutboxRetentionProfile profile,
        double requestP99Milliseconds,
        double workerP99Milliseconds,
        long deletedRows) =>
        new(
            profile,
            MixedLoadLatencyStatistics.Calculate([requestP99Milliseconds]),
            MixedLoadLatencyStatistics.Calculate([workerP99Milliseconds]),
            deletedRows,
            deletedRows,
            deletedRows,
            0,
            0,
            deletedRows == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate([1d]),
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
