using System.Collections.Concurrent;
using Full.NET.Benchmarks.Outbox;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.UnitTests.Performance;

[TestClass]
public sealed class OutboxCapacityContractTests
{
    [TestMethod]
    public void Defaults_freeze_dual_database_capacity_matrix()
    {
        var options = OutboxCapacityOptions.Parse([]);

        CollectionAssert.AreEqual(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 4, 8 },
            options.ConcurrencyLevels.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 10, 100, 1000 },
            options.HandlerDelayMilliseconds.ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            options.ReplicaCounts.ToArray());
        CollectionAssert.AreEqual(
            new[] { 20, 100 },
            options.BatchSizes.ToArray());
        CollectionAssert.AreEqual(
            new[] { 256, 4096 },
            options.PayloadSizes.ToArray());
        Assert.AreEqual(3, options.Repetitions);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.Warmup);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Duration);
        Assert.AreEqual(20_000, options.SeedMessages);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Lease);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.LeaseRenewal);
        Assert.IsTrue(options.RecoveryEnabled);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.RecoveryGrace);
        Assert.IsTrue(options.ResumeEnabled);
        Assert.AreEqual(0, options.MaximumNewSamples);
    }

    [TestMethod]
    public void Matrix_uses_core_capacity_and_bounded_shape_scenarios()
    {
        var scenarios = OutboxCapacityScenarioCatalog.Build(
            OutboxCapacityOptions.Parse([]));

        Assert.HasCount(35, scenarios);
        Assert.AreEqual(35, scenarios.Distinct().Count());
        Assert.HasCount(
            32,
            scenarios.Where(scenario =>
                scenario.BatchSize == 20
                && scenario.PayloadSize == 256));
        Assert.IsTrue(scenarios.Any(scenario =>
            scenario.Concurrency == 8
            && scenario.Replicas == 2
            && scenario.HandlerDelayMilliseconds == 10
            && scenario.BatchSize == 100
            && scenario.PayloadSize == 4096));
    }

    [TestMethod]
    public void Parser_supports_short_affected_smoke_runs()
    {
        var options = OutboxCapacityOptions.Parse(
        [
            "--providers", "mysql",
            "--concurrency", "1,4",
            "--handler-delay-ms", "0,100",
            "--replicas", "1",
            "--batch-sizes", "10",
            "--payload-sizes", "128",
            "--repetitions", "1",
            "--warmup-seconds", "0",
            "--duration-seconds", "2",
            "--seed-messages", "200",
            "--lease-seconds", "9",
            "--lease-renewal-seconds", "3",
            "--recovery", "false",
            "--recovery-grace-seconds", "2",
            "--resume", "false",
            "--output", "artifacts/outbox-capacity",
        ]);

        CollectionAssert.AreEqual(new[] { "mysql" }, options.Providers.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 4 }, options.ConcurrencyLevels.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 100 },
            options.HandlerDelayMilliseconds.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, options.ReplicaCounts.ToArray());
        CollectionAssert.AreEqual(new[] { 10 }, options.BatchSizes.ToArray());
        CollectionAssert.AreEqual(new[] { 128 }, options.PayloadSizes.ToArray());
        Assert.AreEqual(1, options.Repetitions);
        Assert.AreEqual(TimeSpan.Zero, options.Warmup);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.Duration);
        Assert.AreEqual(200, options.SeedMessages);
        Assert.AreEqual(TimeSpan.FromSeconds(9), options.Lease);
        Assert.AreEqual(TimeSpan.FromSeconds(3), options.LeaseRenewal);
        Assert.IsFalse(options.RecoveryEnabled);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.RecoveryGrace);
        Assert.IsFalse(options.ResumeEnabled);
        Assert.AreEqual(0, options.MaximumNewSamples);
        Assert.AreEqual("artifacts/outbox-capacity", options.OutputDirectory);
    }

    [TestMethod]
    public void Parser_rejects_unsafe_or_ambiguous_matrix_shapes()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(["--providers", "postgres"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(["--concurrency", "4,4"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--concurrency", "17"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(
            [
                "--concurrency", "8",
                "--batch-sizes", "4",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--handler-delay-ms", "60001"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--replicas", "0"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--batch-sizes", "201"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--payload-sizes", "63"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(
            [
                "--lease-seconds", "10",
                "--lease-renewal-seconds", "10",
            ]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(["--unknown", "value"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(["--recovery", "sometimes"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(["--resume", "sometimes"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(["--max-new-samples", "-1"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => OutboxCapacityOptions.Parse(
            [
                "--resume", "false",
                "--max-new-samples", "1",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(
            [
                "--recovery", "true",
                "--recovery-grace-seconds", "0",
            ]));
    }

    [TestMethod]
    public void Run_budget_stops_after_configured_new_samples()
    {
        var options = OutboxCapacityOptions.Parse(
        [
            "--max-new-samples", "2",
        ]);
        var budget = new OutboxCapacityRunBudget(
            options.MaximumNewSamples);
        var unlimited = new OutboxCapacityRunBudget(
            maximumNewSamples: 0);

        Assert.AreEqual(2, options.MaximumNewSamples);
        Assert.IsFalse(budget.RecordCompletedSample());
        Assert.IsTrue(budget.RecordCompletedSample());
        Assert.IsTrue(budget.IsExhausted);
        Assert.AreEqual(2, budget.CompletedSamples);
        Assert.IsFalse(unlimited.RecordCompletedSample());
        Assert.IsFalse(unlimited.IsExhausted);
    }

    [TestMethod]
    public async Task Checkpoint_resume_skips_completed_keys_and_rejects_parameter_drift()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-outbox-capacity-checkpoint-{Guid.NewGuid():N}");
        var options = OutboxCapacityOptions.Parse(
        [
            "--providers", "sqlserver",
            "--concurrency", "1",
            "--handler-delay-ms", "0",
            "--replicas", "1",
            "--batch-sizes", "20",
            "--payload-sizes", "256",
            "--repetitions", "2",
            "--warmup-seconds", "0",
            "--duration-seconds", "1",
            "--seed-messages", "20",
            "--lease-seconds", "6",
            "--lease-renewal-seconds", "2",
            "--recovery", "false",
            "--resume", "true",
            "--output", outputDirectory,
        ]);
        var scenarios = OutboxCapacityScenarioCatalog.Build(options);
        var completedRun = CreateCompletedRun(scenarios.Single());

        try
        {
            await OutboxCapacityReportWriter.WriteAsync(
                options,
                scenarios,
                [
                    new OutboxCapacityProviderResult(
                        "SqlServer",
                        "sqlserver:test",
                        "test",
                        [completedRun],
                        []),
                ],
                CancellationToken.None);

            var checkpoint = await OutboxCapacityCheckpoint.LoadAsync(
                options with
                {
                    MaximumNewSamples = 1,
                },
                scenarios,
                CancellationToken.None);

            Assert.IsTrue(checkpoint.HasRun(
                "sqlserver",
                scenarios.Single(),
                repetition: 1));
            Assert.IsFalse(checkpoint.HasRun(
                "sqlserver",
                scenarios.Single(),
                repetition: 2));
            Assert.AreEqual(1, checkpoint.CompletedRunCount);
            Assert.AreEqual(1, checkpoint.PendingRunCount);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => OutboxCapacityCheckpoint.LoadAsync(
                    options with
                    {
                        Duration = TimeSpan.FromSeconds(2),
                    },
                    scenarios,
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

    [TestMethod]
    public void Recovery_gate_requires_same_message_second_attempt_and_lease_bounded_time()
    {
        var messageId = Guid.CreateVersion7();
        var passing = OutboxCapacityRecoveryResult.Create(
            provider: "mysql",
            repetition: 1,
            abandonedMessageId: messageId,
            recoveredMessageId: messageId,
            recoveryDuration: TimeSpan.FromSeconds(6.1),
            lease: TimeSpan.FromSeconds(6),
            recoveryGrace: TimeSpan.FromSeconds(2),
            attempts: 2,
            duplicateDeliveries: 1,
            dapperFailures: 0,
            pendingBefore: 1,
            pendingAfter: 0,
            dapperCancellations: 0,
            acquireExecutions: 3);
        var tooEarly = passing with
        {
            RecoveryDurationMilliseconds = 4_000,
        };
        var missingAcquireEvidence = passing with
        {
            AcquireExecutions = 0,
        };
        var cancelledAcquire = passing with
        {
            DapperCancellations = 1,
        };

        Assert.IsTrue(passing.CorrectnessGatePassed);
        Assert.IsFalse(tooEarly.CorrectnessGatePassed);
        Assert.IsFalse(missingAcquireEvidence.CorrectnessGatePassed);
        Assert.IsFalse(cancelledAcquire.CorrectnessGatePassed);
        Assert.AreEqual(
            3L,
            OutboxCapacityRecoveryResult.CountAcquireExecutions(
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    ["outbox.acquire.sql_server"] = 2,
                    ["outbox.select_claimable_ids.my_sql"] = 1,
                    ["outbox.mark_processed"] = 1,
                }));
    }

    [TestMethod]
    public async Task Benchmark_host_registers_every_real_outbox_store_dependency()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = "SqlServer",
                [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                    "Server=(local);Database=fullnet;Integrated Security=true",
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    "Binary16",
            })
            .Build();
        var services = new ServiceCollection();

        OutboxCapacityServiceRegistration.Add(
            services,
            configuration,
            new NoOpCapacityHandler());

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        Assert.IsNotNull(provider.GetService<IIdGenerator>());
        await using var scope = provider.CreateAsyncScope();
        Assert.IsNotNull(scope.ServiceProvider.GetService<IOutboxStore>());
    }

    [TestMethod]
    public void Processor_logger_captures_message_failure_exception()
    {
        var errors = new ConcurrentQueue<string>();
        var logger = new OutboxCapacityProcessorLogger<object>(errors);
        var exception = new InvalidOperationException(
            "terminal write failed\r\nretry");

        logger.Log(
            LogLevel.Warning,
            new EventId(3002),
            "message failed",
            exception,
            static (state, _) => state);

        CollectionAssert.AreEqual(
            new[]
            {
                "outbox.event.3002 | InvalidOperationException: terminal write failed retry",
            },
            errors.ToArray());
    }

    private static OutboxCapacityRunResult CreateCompletedRun(
        OutboxCapacityScenario scenario) => new(
        Provider: "SqlServer",
        ContainerImage: "sqlserver:test",
        DatabaseVersion: "test",
        Scenario: scenario,
        Repetition: 1,
        ActualDurationSeconds: 1,
        CompletedMessages: 1,
        UniqueMessages: 1,
        DuplicateDeliveries: 0,
        MessagesPerSecond: 1,
        HandlerLatency: null,
        LeaseRenewalExecutions: 0,
        Dapper: new MixedLoadDapperSnapshot(
            new Dictionary<string, long>(),
            Duration: null,
            Failures: 0),
        ConnectionPool: new MixedLoadConnectionPoolSnapshot(
            ConfiguredMaximumConnections: 100,
            PeakActiveConnections: 1,
            PeakIdleConnections: 0,
            PeakPooledConnections: 1,
            PeakPendingRequests: 0,
            PeakStasisConnections: 0,
            ConnectionTimeouts: 0,
            ReclaimedConnections: 0,
            WaitDuration: null,
            PublishedInstruments: [],
            MaximumSafeActiveConnections: 80,
            CapacityHeadroomPassed: true,
            ObservationMode: "test",
            EvidenceComplete: true,
            EvidenceError: null),
        DatabaseContainer: new MixedLoadContainerSnapshot(
            SampleCount: 1,
            AverageCpuPercentOfHost: 0,
            PeakCpuPercentOfHost: 0,
            PeakMemoryBytes: 0,
            EvidenceComplete: true,
            EvidenceError: null),
        Process: new MixedLoadProcessDelta(
            CpuPercent: 0,
            AllocatedBytes: 0,
            FinalHeapSizeBytes: 0,
            Gen0Collections: 0,
            Gen1Collections: 0,
            Gen2Collections: 0),
        DatabaseBefore: CreateDatabaseSnapshot(pendingOutboxCount: 2),
        DatabaseAfter: CreateDatabaseSnapshot(pendingOutboxCount: 1),
        ProcessorErrors: []);

    private static MixedLoadDatabaseSnapshot CreateDatabaseSnapshot(
        long pendingOutboxCount) => new(
        CapturedAtUtc: DateTimeOffset.UtcNow,
        AccessLogCount: 0,
        PendingOutboxCount: pendingOutboxCount,
        OldestPendingOutboxAtUtc: DateTimeOffset.UtcNow,
        DatabaseSessions: 1,
        ActiveLocks: 0,
        LockWaitCount: 0,
        LockWaitMilliseconds: 0,
        MetricsError: null);

    private sealed class NoOpCapacityHandler : IIntegrationEventHandler
    {
        public string EventType => "benchmark.outbox.capacity";

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
