using Full.NET.Benchmarks.Outbox;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OutboxCapacityOptions.Parse(
            [
                "--recovery", "true",
                "--recovery-grace-seconds", "0",
            ]));
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
