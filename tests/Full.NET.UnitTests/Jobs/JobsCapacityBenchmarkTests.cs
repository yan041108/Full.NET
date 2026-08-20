using Full.NET.Benchmarks.Jobs;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsCapacityBenchmarkTests
{
    [TestMethod]
    public void Defaults_create_bounded_manual_ci_matrix()
    {
        var options = JobsCapacityOptions.Parse([]);

        CollectionAssert.AreEqual(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 4, 8 },
            options.ConcurrencyLevels.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1000 },
            options.HandlerDelayMilliseconds.ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            options.ReplicaCounts.ToArray());
        Assert.AreEqual(3, options.Repetitions);
        Assert.AreEqual(16, options.BatchSize);
        Assert.AreEqual(8, options.HandlerKeyCount);
        Assert.AreEqual(1, options.FailingHandlerKeyCount);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Lease);
        Assert.AreEqual(
            TimeSpan.FromSeconds(5),
            options.LeaseRenewal);
    }

    [TestMethod]
    public void Catalog_builds_single_replica_ab_and_one_slow_replica_shape()
    {
        var scenarios = JobsCapacityScenarioCatalog.Build(
            JobsCapacityOptions.Parse([]));

        Assert.HasCount(9, scenarios);
        Assert.IsTrue(
            scenarios.Contains(new JobsCapacityScenario(2, 1000, 2)));
        Assert.IsFalse(
            scenarios.Contains(new JobsCapacityScenario(8, 0, 2)));
    }

    [TestMethod]
    public void Backlog_planner_keeps_warmup_rate_sustained_with_safety_margin()
    {
        var required = JobsCapacityBacklogPlanner.CalculateRequiredJobs(
            configuredMinimum: 64,
            completedDuringWarmup: 40,
            warmup: TimeSpan.FromSeconds(2),
            duration: TimeSpan.FromSeconds(10),
            batchSize: 16,
            replicas: 2);

        Assert.AreEqual(364, required);
    }

    [TestMethod]
    public void Statistics_use_nearest_rank_for_tail_latency()
    {
        var statistics = JobsCapacityStatistics.Calculate(
            Enumerable.Range(1, 100)
                .Select(value => (double)value)
                .ToArray());

        Assert.AreEqual(50d, statistics.P50Milliseconds);
        Assert.AreEqual(95d, statistics.P95Milliseconds);
        Assert.AreEqual(99d, statistics.P99Milliseconds);
    }

    [TestMethod]
    public void Correctness_gate_rejects_invalid_measurement_scalars()
    {
        var run = CreatePassingRun(
            provider: "sqlserver",
            new JobsCapacityScenario(
                Concurrency: 1,
                HandlerDelayMilliseconds: 0,
                Replicas: 1),
            repetition: 1,
            terminalsPerSecond: 1d,
            queueP95Milliseconds: 100d,
            leaseRenewalExecutions: 0);

        Assert.IsTrue(run.CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            ActualDurationSeconds = 0d,
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            TerminalsPerSecond = double.NaN,
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DrainDuration = TimeSpan.FromMilliseconds(-1),
        }).CorrectnessGatePassed);
    }

    [TestMethod]
    public void Correctness_gate_rejects_invalid_latency_evidence()
    {
        var run = CreatePassingRun(
            provider: "sqlserver",
            new JobsCapacityScenario(
                Concurrency: 1,
                HandlerDelayMilliseconds: 0,
                Replicas: 1),
            repetition: 1,
            terminalsPerSecond: 1d,
            queueP95Milliseconds: 100d,
            leaseRenewalExecutions: 0);

        Assert.IsTrue(run.CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            HandlerLatency = null,
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            QueueLatency = run.QueueLatency! with
            {
                P95Milliseconds = double.NaN,
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            QueueLatency = run.QueueLatency! with
            {
                SampleCount = 9,
            },
        }).CorrectnessGatePassed);
    }

    [TestMethod]
    public void Correctness_gate_rejects_invalid_resource_evidence()
    {
        var run = CreatePassingRun(
            provider: "sqlserver",
            new JobsCapacityScenario(
                Concurrency: 1,
                HandlerDelayMilliseconds: 0,
                Replicas: 1),
            repetition: 1,
            terminalsPerSecond: 1d,
            queueP95Milliseconds: 100d,
            leaseRenewalExecutions: 0);

        Assert.IsTrue(run.CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            Process = null,
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            Process = run.Process! with
            {
                CpuPercent = double.NaN,
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DatabaseBefore = null,
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DatabaseAfter = run.DatabaseAfter! with
            {
                MetricsError = "permission_denied",
            },
        }).CorrectnessGatePassed);
    }

    [TestMethod]
    public void Correctness_gate_rejects_invalid_pool_and_container_evidence()
    {
        var run = CreatePassingRun(
            provider: "sqlserver",
            new JobsCapacityScenario(
                Concurrency: 1,
                HandlerDelayMilliseconds: 0,
                Replicas: 1),
            repetition: 1,
            terminalsPerSecond: 1d,
            queueP95Milliseconds: 100d,
            leaseRenewalExecutions: 0);

        Assert.IsTrue(run.CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            ConnectionPool = run.ConnectionPool with
            {
                PeakActiveConnections = double.NaN,
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            ConnectionPool = run.ConnectionPool with
            {
                EvidenceError = "spoofed_complete_evidence",
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DatabaseContainer = run.DatabaseContainer with
            {
                SampleCount = 0,
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DatabaseContainer = run.DatabaseContainer with
            {
                AverageCpuPercentOfHost = double.NaN,
            },
        }).CorrectnessGatePassed);
        Assert.IsFalse((run with
        {
            DatabaseContainer = run.DatabaseContainer with
            {
                PeakCpuPercentOfHost = 0d,
            },
        }).CorrectnessGatePassed);
    }

    [TestMethod]
    public async Task Probe_records_fixed_key_failures_without_high_cardinality_data()
    {
        var probe = new JobsCapacityProbe();
        var scopeId = Guid.CreateVersion7();
        var handler = new JobsCapacityHandler(
            "jobs.benchmark.capacity.failure.0",
            TimeSpan.Zero,
            fails: true,
            scopeId,
            probe);

        await Assert.ThrowsExactlyAsync<
            JobsCapacityExpectedFailureException>(
            () => handler.ExecuteAsync(
                new JobExecutionContext(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    handler.HandlerKind,
                    handler.HandlerKind,
                    null,
                    JobTriggerKinds.Manual),
                CancellationToken.None));

        var snapshot = probe.Snapshot();
        Assert.AreEqual(1L, snapshot.Invocations);
        Assert.AreEqual(1L, snapshot.ExpectedFailures);
        CollectionAssert.AreEqual(
            new[] { scopeId },
            snapshot.ScopeIds.ToArray());
    }

    [TestMethod]
    public void Assessment_requires_both_providers_and_all_c2_safety_gates()
    {
        var options = JobsCapacityOptions.Parse([]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        var assessment = JobsCapacityAssessment.Assess(
            options,
            scenarios,
            CreatePassingDualProviderRuns());

        Assert.AreEqual(
            JobsCapacityRecommendation.EligibleForCanaryAtTwo,
            assessment.Recommendation);
    }

    [TestMethod]
    public void Assessment_keeps_one_when_configured_matrix_is_partial()
    {
        var options = JobsCapacityOptions.Parse([]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        var completeRuns = CreatePassingDualProviderRuns();
        var duplicate = completeRuns.Single(run =>
            run.Provider == "sqlserver"
            && run.Scenario.Concurrency == 8
            && run.Scenario.HandlerDelayMilliseconds == 0
            && run.Scenario.Replicas == 1
            && run.Repetition == options.Repetitions);
        var runs = completeRuns
            .Where(run =>
                !(run.Provider == "mysql"
                    && run.Scenario.Concurrency == 8
                    && run.Scenario.HandlerDelayMilliseconds == 0
                    && run.Scenario.Replicas == 1
                    && run.Repetition == options.Repetitions))
            .Append(duplicate)
            .ToArray();

        Assert.AreEqual(completeRuns.Count, runs.Length);
        var assessment = JobsCapacityAssessment.Assess(
            options,
            scenarios,
            runs);

        Assert.AreEqual(
            JobsCapacityRecommendation.KeepConcurrencyOne,
            assessment.Recommendation);
        Assert.IsTrue(assessment.Reasons.Any(reason =>
            reason.Contains("矩阵证据不完整", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Assessment_keeps_one_when_local_smoke_matrix_is_complete()
    {
        var options = JobsCapacityOptions.Parse(
        [
            "--providers", "sqlserver,mysql",
            "--concurrency", "1,2",
            "--handler-delay-ms", "1000",
            "--replicas", "1,2",
            "--repetitions", "1",
            "--warmup-seconds", "1",
            "--duration-seconds", "2",
            "--seed-jobs", "64",
            "--batch-size", "8",
            "--handler-keys", "4",
            "--failing-handler-keys", "1",
        ]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        var assessment = JobsCapacityAssessment.Assess(
            options,
            scenarios,
            CreatePassingDualProviderRuns(options, scenarios));

        Assert.AreEqual(
            JobsCapacityRecommendation.KeepConcurrencyOne,
            assessment.Recommendation);
        Assert.IsTrue(assessment.Reasons.Any(reason =>
            reason.Contains("手工决策矩阵", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Assessment_keeps_one_when_mysql_has_database_failure()
    {
        var options = JobsCapacityOptions.Parse([]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        var runs = CreatePassingDualProviderRuns()
            .Select(run =>
                run.Provider == "mysql"
                && run.Scenario.Concurrency == 2
                    ? run with
                    {
                        Dapper = run.Dapper with { Failures = 1 },
                    }
                    : run)
            .ToArray();

        var assessment = JobsCapacityAssessment.Assess(
            options,
            scenarios,
            runs);

        Assert.AreEqual(
            JobsCapacityRecommendation.KeepConcurrencyOne,
            assessment.Recommendation);
    }

    [TestMethod]
    public async Task Runtime_registers_fixed_handlers_with_one_identity_per_scope()
    {
        var options = JobsCapacityOptions.Parse([]);
        var scenario = new JobsCapacityScenario(
            Concurrency: 2,
            HandlerDelayMilliseconds: 0,
            Replicas: 1);
        var probe = new JobsCapacityProbe();
        await using var services = JobsCapacityRuntime.BuildServices(
            provider: "sqlserver",
            connectionString:
                "Server=localhost;Database=fullnet;User Id=test;Password=test;TrustServerCertificate=True",
            poolName: "fullnet-jobs-capacity-test",
            scenario,
            options,
            probe);
        await using var scope = services.CreateAsyncScope();
        var handlers = scope.ServiceProvider
            .GetServices<IJobHandlerExecutor>()
            .Where(handler => handler.HandlerKind.StartsWith(
                "jobs.benchmark.capacity.",
                StringComparison.Ordinal))
            .OrderBy(handler => handler.HandlerKind, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(options.HandlerKeyCount, handlers);
        var context = new JobExecutionContext(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            handlers[0].HandlerKind,
            handlers[0].HandlerKind,
            null,
            JobTriggerKinds.Manual);
        await Assert.ThrowsExactlyAsync<
            JobsCapacityExpectedFailureException>(
            () => handlers[0].ExecuteAsync(context, CancellationToken.None));
        await handlers[^1].ExecuteAsync(context, CancellationToken.None);
        Assert.HasCount(1, probe.Snapshot().ScopeIds);
    }

    [TestMethod]
    public async Task Report_writer_is_atomic_and_checkpoint_rejects_another_build()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-jobs-capacity-{Guid.NewGuid():N}");
        var options = JobsCapacityOptions.Parse(
        [
            "--output", outputDirectory,
        ]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        try
        {
            await JobsCapacityReportWriter.WriteAsync(
                options,
                scenarios,
                providers: [],
                CancellationToken.None);

            Assert.IsTrue(File.Exists(
                Path.Combine(outputDirectory, "report.json")));
            Assert.IsTrue(File.Exists(
                Path.Combine(outputDirectory, "summary.md")));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => JobsCapacityCheckpoint.LoadAsync(
                    options,
                    scenarios,
                    buildFingerprint: "different-build",
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static IReadOnlyList<JobsCapacityRunResult>
        CreatePassingDualProviderRuns()
    {
        var options = JobsCapacityOptions.Parse([]);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        return CreatePassingDualProviderRuns(options, scenarios);
    }

    private static IReadOnlyList<JobsCapacityRunResult>
        CreatePassingDualProviderRuns(
            JobsCapacityOptions options,
            IReadOnlyList<JobsCapacityScenario> scenarios)
    {
        return new[] { "sqlserver", "mysql" }
            .SelectMany(provider => scenarios.SelectMany(scenario =>
                Enumerable.Range(1, options.Repetitions).Select(repetition =>
                    CreatePassingRun(
                        provider,
                        scenario,
                        repetition,
                        terminalsPerSecond:
                            scenario.Concurrency == 1 ? 100d : 125d,
                        queueP95Milliseconds:
                            scenario.Concurrency == 1 ? 100d : 90d,
                        leaseRenewalExecutions:
                            scenario.HandlerDelayMilliseconds
                                == options.HandlerDelayMilliseconds.Max()
                                ? 1
                                : 0))))
            .ToArray();
    }

    private static JobsCapacityRunResult CreatePassingRun(
        string provider,
        JobsCapacityScenario scenario,
        int repetition,
        double terminalsPerSecond,
        double queueP95Milliseconds,
        long leaseRenewalExecutions) =>
        new(
            provider,
            scenario,
            repetition,
            ActualDurationSeconds: 10d,
            TerminalExecutions: 10,
            SucceededExecutions: 9,
            FailedExecutions: 1,
            PendingExecutions: 10,
            RunningExecutions: 0,
            TerminalExecutionsWithLease: 0,
            AttemptCountGreaterThanOne: 0,
            HandlerInvocations: 10,
            HandlerExpectedFailures: 1,
            terminalsPerSecond,
            HandlerLatency: JobsCapacityStatistics.Calculate(
                Enumerable.Repeat(1d, 10).ToArray()),
            QueueLatency: JobsCapacityStatistics.Calculate(
                Enumerable.Repeat(
                    queueP95Milliseconds,
                    10).ToArray()),
            leaseRenewalExecutions,
            Dapper: new MixedLoadDapperSnapshot(
                new Dictionary<string, long>(),
                Duration: null,
                Failures: 0,
                Cancellations: 0),
            ConnectionPool: PassingConnectionPool(),
            DatabaseContainer: new MixedLoadContainerSnapshot(
                SampleCount: 1,
                AverageCpuPercentOfHost: 1d,
                PeakCpuPercentOfHost: 1d,
                PeakMemoryBytes: 1,
                EvidenceComplete: true,
                EvidenceError: null),
            ProcessorErrors: [])
        {
            Process = new MixedLoadProcessDelta(
                CpuPercent: 1d,
                AllocatedBytes: 1,
                FinalHeapSizeBytes: 1,
                Gen0Collections: 0,
                Gen1Collections: 0,
                Gen2Collections: 0),
            DatabaseBefore = PassingDatabaseSnapshot(
                DateTimeOffset.UnixEpoch),
            DatabaseAfter = PassingDatabaseSnapshot(
                DateTimeOffset.UnixEpoch.AddSeconds(10)),
        };

    private static MixedLoadDatabaseSnapshot PassingDatabaseSnapshot(
        DateTimeOffset capturedAtUtc) =>
        new(
            capturedAtUtc,
            AccessLogCount: 0,
            PendingOutboxCount: 0,
            OldestPendingOutboxAtUtc: null,
            DatabaseSessions: 1,
            ActiveLocks: 0,
            LockWaitCount: 0,
            LockWaitMilliseconds: 0d,
            MetricsError: null);

    private static MixedLoadConnectionPoolSnapshot
        PassingConnectionPool() =>
        new(
            ConfiguredMaximumConnections: 100,
            PeakActiveConnections: 2,
            PeakIdleConnections: 2,
            PeakPooledConnections: 4,
            PeakPendingRequests: 0,
            PeakStasisConnections: 0,
            ConnectionTimeouts: 0,
            ReclaimedConnections: 0,
            WaitDuration: null,
            PublishedInstruments: ["test"],
            MaximumSafeActiveConnections: 90,
            CapacityHeadroomPassed: true,
            ObservationMode: "test",
            EvidenceComplete: true,
            EvidenceError: null);
}
