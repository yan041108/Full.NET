extern alias workerhost;

using System.Data;
using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using global::MemoryPack;
using WorkerHost = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed partial class OutboxRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_outbox_dead_letters_unknown_version_and_processes_next_message()
    {
        await VerifyUnknownVersionDeadLetterAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_dead_letters_unknown_version_and_processes_next_message()
    {
        await VerifyUnknownVersionDeadLetterAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_outbox_dead_letters_invalid_payload_without_retry()
    {
        await VerifyInvalidPayloadDeadLetterAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_dead_letters_invalid_payload_without_retry()
    {
        await VerifyInvalidPayloadDeadLetterAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_outbox_lease_prevents_duplicate_success_and_recovers_after_expiry()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await VerifyLeaseRecoveryAsync(
            DatabaseProvider.SqlServer,
            connectionString);
        await VerifyTerminalUpdateDoesNotBlockAcquireAsync(
            DatabaseProvider.SqlServer,
            connectionString);
        await VerifyBoundedConcurrencyUsesIndependentScopesAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_lease_prevents_duplicate_success_and_recovers_after_expiry()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await VerifyLeaseRecoveryAsync(
            DatabaseProvider.MySql,
            connectionString);
        await VerifyTerminalUpdateDoesNotBlockAcquireAsync(
            DatabaseProvider.MySql,
            connectionString);
        await VerifyBoundedConcurrencyUsesIndependentScopesAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_outbox_active_renewal_protects_batch_tail()
    {
        await VerifyActiveLeaseRenewalProtectsBatchTailAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_active_renewal_protects_batch_tail()
    {
        await VerifyActiveLeaseRenewalProtectsBatchTailAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_outbox_backlog_snapshot_tracks_pending_count_and_oldest_age()
    {
        await VerifyBacklogSnapshotAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_backlog_snapshot_tracks_pending_count_and_oldest_age()
    {
        await VerifyBacklogSnapshotAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_outbox_retention_deletes_only_expired_successful_messages()
    {
        await VerifyRetentionAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_retention_deletes_only_expired_successful_messages()
    {
        await VerifyRetentionAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_version_retirement_snapshot_counts_only_target_unprocessed_routes()
    {
        await VerifyVersionRetirementSnapshotAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_version_retirement_snapshot_counts_only_target_unprocessed_routes()
    {
        await VerifyVersionRetirementSnapshotAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyUnknownVersionDeadLetterAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        var handler = new RecordingHandler();
        await using var services = BuildServices(configuration, clock, handler);
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            2,
            new TestIntegrationEvent("legacy"));
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("current"));

        var processor = CreateProcessor(services, clock);
        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, handler.HandledCount);
        Assert.AreEqual(
            1L,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                SchemaVersion = 2
                AND ProcessedAtUtc IS NULL
                AND DeadLetteredAtUtc IS NOT NULL
                AND DeadLetterReasonCode = @Reason
                """,
                new { Reason = OutboxDeadLetterReasons.HandlerNotFound }));
        Assert.AreEqual(
            1L,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                SchemaVersion = 1
                AND ProcessedAtUtc IS NOT NULL
                AND DeadLetteredAtUtc IS NULL
                """));
    }

    private static async Task VerifyInvalidPayloadDeadLetterAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        await using var services = BuildServices(
            configuration,
            clock,
            new DeserializingHandler(new MemoryPackIntegrationEventSerializer()));
        await InsertOutboxAsync(
            services,
            DeserializingHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("poison"));
        await OverwritePayloadAsync(
            databaseProvider,
            connectionString,
            DeserializingHandler.EventTypeValue,
            1,
            [0xC1]);

        var processor = CreateProcessor(services, clock);
        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(
            1L,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                MessageType = @MessageType
                AND SchemaVersion = 1
                AND ProcessedAtUtc IS NULL
                AND NextAttemptAtUtc IS NULL
                AND DeadLetteredAtUtc IS NOT NULL
                AND DeadLetterReasonCode = @Reason
                """,
                new
                {
                    MessageType = DeserializingHandler.EventTypeValue,
                    Reason = OutboxDeadLetterReasons.InvalidPayload
                }));
    }

    private static async Task VerifyLeaseRecoveryAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        await using var services = BuildServices(configuration, clock);
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("lease"));

        OutboxEnvelope firstLease;
        await using (var firstScope = services.CreateAsyncScope())
        {
            firstScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var store = firstScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var leased = await store.AcquireAsync(1, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.HasCount(1, leased);
            firstLease = leased[0];
        }

        await using (var concurrentScope = services.CreateAsyncScope())
        {
            concurrentScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var store = concurrentScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var leased = await store.AcquireAsync(1, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.AreEqual(0, leased.Count);
        }

        clock.UtcNow = clock.UtcNow.AddSeconds(31);

        await using (var recoveryScope = services.CreateAsyncScope())
        {
            recoveryScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var store = recoveryScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var leased = await store.AcquireAsync(1, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.HasCount(1, leased);
            Assert.AreEqual(firstLease.Id, leased[0].Id);
            Assert.AreNotEqual(firstLease.LockId, leased[0].LockId);
            Assert.AreEqual(2, leased[0].Attempts);
            await store.MarkProcessedAsync(leased[0].Id, leased[0].LockId, CancellationToken.None);
        }

        Assert.AreEqual(
            1L,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                "ProcessedAtUtc IS NOT NULL AND DeadLetteredAtUtc IS NULL"));
        Assert.AreEqual(
            2,
            await ReadAttemptsAsync(databaseProvider, connectionString));
    }

    private static async Task VerifyActiveLeaseRenewalProtectsBatchTailAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        const int messageCount = 2;
        const int leaseSeconds = 6;
        var initialNow =
            new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(initialNow);
        var configuration = CreateConfiguration(
            databaseProvider,
            connectionString);
        await MigrateAsync(databaseProvider, connectionString);
        var handler = new BatchTailLeaseHandler();
        await using var services = BuildServices(
            configuration,
            clock,
            handler);
        for (var index = 0; index < messageCount; index++)
        {
            await InsertOutboxAsync(
                services,
                BatchTailLeaseHandler.EventTypeValue,
                1,
                new TestIntegrationEvent($"lease-renewal-{index}"));
        }

        var processor = CreateProcessor(
            services,
            clock,
            new WorkerHost.OutboxWorkerOptions
            {
                BatchSize = messageCount,
                MaxConcurrency = 1,
                LeaseSeconds = leaseSeconds,
                LeaseRenewalSeconds = 1,
                PollMilliseconds = 1000,
                MaxAttempts = 3,
            });
        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await handler.FirstMessageEntered.WaitAsync(TimeSpan.FromSeconds(5));
        clock.UtcNow = initialNow.AddSeconds(leaseSeconds + 1);

        try
        {
            await WaitForLeaseExtensionAsync(
                databaseProvider,
                connectionString,
                clock.UtcNow,
                messageCount);
            var leaseBeforeClockRollback = await ReadActiveLeaseAsync(
                databaseProvider,
                connectionString);
            Assert.HasCount(messageCount, leaseBeforeClockRollback);
            Assert.AreEqual(
                1,
                leaseBeforeClockRollback
                    .Select(row => row.LockId)
                    .Distinct()
                    .Count());

            clock.UtcNow = initialNow.AddMinutes(-1);
            await using (var renewalScope = services.CreateAsyncScope())
            {
                renewalScope.ServiceProvider
                    .GetRequiredService<CurrentTenantAccessor>()
                    .SetHost();
                var renewalStore = renewalScope.ServiceProvider
                    .GetRequiredService<IOutboxStore>();
                await renewalStore.RenewLeaseAsync(
                    leaseBeforeClockRollback
                        .Select(row => row.Id)
                        .ToArray(),
                    leaseBeforeClockRollback[0].LockId,
                    TimeSpan.FromSeconds(leaseSeconds),
                    CancellationToken.None);
            }

            var leaseAfterClockRollback = await ReadActiveLeaseAsync(
                databaseProvider,
                connectionString);
            Assert.HasCount(messageCount, leaseAfterClockRollback);
            Assert.IsTrue(
                leaseAfterClockRollback.Min(row => row.LockedUntilUtc)
                >= leaseBeforeClockRollback.Min(row => row.LockedUntilUtc),
                "????????????????????????");
            clock.UtcNow = initialNow.AddSeconds(leaseSeconds + 1);

            await using var competingScope = services.CreateAsyncScope();
            competingScope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>()
                .SetHost();
            var competingStore = competingScope.ServiceProvider
                .GetRequiredService<IOutboxStore>();

            var competingLease = await competingStore.AcquireAsync(
                messageCount,
                TimeSpan.FromSeconds(leaseSeconds),
                CancellationToken.None);

            Assert.AreEqual(
                0,
                competingLease.Count,
                "????????????????????????????");
        }
        finally
        {
            handler.ReleaseFirstMessage();
        }

        Assert.AreEqual(
            messageCount,
            await processingTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(messageCount, handler.HandledCount);
        Assert.AreEqual(
            messageCount,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                MessageType = @MessageType
                AND Attempts = 1
                AND ProcessedAtUtc IS NOT NULL
                AND DeadLetteredAtUtc IS NULL
                """,
                new { MessageType = BatchTailLeaseHandler.EventTypeValue }));
    }

    private static async Task WaitForLeaseExtensionAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        DateTimeOffset threshold,
        int expectedCount)
    {
        var databaseThreshold = databaseProvider == DatabaseProvider.MySql
            ? (object)threshold.UtcDateTime
            : threshold;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var extendedCount = await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                ProcessedAtUtc IS NULL
                AND DeadLetteredAtUtc IS NULL
                AND LockId IS NOT NULL
                AND LockedUntilUtc > @Threshold
                """,
                new { Threshold = databaseThreshold });
            if (extendedCount == expectedCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail("Outbox ??????????????????");
    }

    private static async Task<IReadOnlyList<ActiveLeaseRow>>
        ReadActiveLeaseAsync(
            DatabaseProvider databaseProvider,
            string connectionString)
    {
        await using var connection = CreateConnection(
            databaseProvider,
            connectionString);
        var rows = await connection.QueryAsync<ActiveLeaseRow>(
            """
            SELECT Id, LockId, LockedUntilUtc
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
              AND LockId IS NOT NULL
            ORDER BY Id;
            """);
        return rows.ToArray();
    }

    private static async Task VerifyTerminalUpdateDoesNotBlockAcquireAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var clock = new MutableClock(
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));
        var configuration = CreateConfiguration(
            databaseProvider,
            connectionString);
        await using var services = BuildServices(configuration, clock);
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("terminal-lock"));

        OutboxEnvelope leased;
        await using (var leaseScope = services.CreateAsyncScope())
        {
            leaseScope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>()
                .SetHost();
            var store = leaseScope.ServiceProvider
                .GetRequiredService<IOutboxStore>();
            var rows = await store.AcquireAsync(
                1,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            Assert.HasCount(1, rows);
            leased = rows[0];
        }

        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        await using var terminalConnection = CreateConnection(
            databaseProvider,
            connectionString);
        await terminalConnection.OpenAsync();
        await using var terminalTransaction = await terminalConnection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var databaseNow = databaseProvider == DatabaseProvider.MySql
            ? (object)clock.UtcNow.UtcDateTime
            : clock.UtcNow;
        var affectedRows = await terminalConnection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET ProcessedAtUtc = @Now,
                NextAttemptAtUtc = NULL,
                LockId = NULL,
                LockedUntilUtc = NULL,
                Error = NULL,
                DeadLetteredAtUtc = NULL,
                DeadLetterReasonCode = NULL
            WHERE Id = @Id
              AND LockId = @LockId
              AND ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
            """,
            new
            {
                leased.Id,
                leased.LockId,
                Now = databaseNow,
            },
            terminalTransaction);
        Assert.AreEqual(1, affectedRows);

        try
        {
            await using var acquireScope = services.CreateAsyncScope();
            acquireScope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>()
                .SetHost();
            var store = acquireScope.ServiceProvider
                .GetRequiredService<IOutboxStore>();
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(3));

            // ???????????Worker ?????????????????????????
            var rows = await store.AcquireAsync(
                1,
                TimeSpan.FromSeconds(30),
                timeout.Token);

            Assert.AreEqual(0, rows.Count);
        }
        finally
        {
            await terminalTransaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task VerifyBoundedConcurrencyUsesIndependentScopesAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        const int messageCount = 4;
        const int maxConcurrency = 2;
        var clock = new MutableClock(
            new DateTimeOffset(2026, 7, 28, 2, 0, 0, TimeSpan.Zero));
        var configuration = CreateConfiguration(
            databaseProvider,
            connectionString);
        await MigrateAsync(databaseProvider, connectionString);
        var probe = new OutboxConcurrencyProbe(maxConcurrency);
        await using var services = BuildServices(
            configuration,
            clock,
            serviceCollection =>
            {
                serviceCollection.AddSingleton(probe);
                serviceCollection.AddScoped<
                    IIntegrationEventHandler,
                    CoordinatedConcurrencyHandler>();
            });
        for (var index = 0; index < messageCount; index++)
        {
            await InsertOutboxAsync(
                services,
                CoordinatedConcurrencyHandler.EventTypeValue,
                1,
                new TestIntegrationEvent($"concurrent-{index}"));
        }

        var processor = CreateProcessor(
            services,
            clock,
            new WorkerHost.OutboxWorkerOptions
            {
                BatchSize = messageCount,
                MaxConcurrency = maxConcurrency,
                LeaseSeconds = 30,
                PollMilliseconds = 1000,
                MaxAttempts = 3,
            });
        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await probe.ConcurrencyReached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(maxConcurrency, probe.PeakConcurrency);
        probe.Release();

        var processed = await processingTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(messageCount, processed);
        Assert.AreEqual(messageCount, probe.HandlerInstanceCount);
        Assert.AreEqual(messageCount, probe.HandledCount);
        Assert.AreEqual(
            messageCount,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                """
                MessageType = @MessageType
                AND ProcessedAtUtc IS NOT NULL
                AND DeadLetteredAtUtc IS NULL
                """,
                new
                {
                    MessageType =
                        CoordinatedConcurrencyHandler.EventTypeValue,
                }));
    }

    private static async Task VerifyBacklogSnapshotAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var firstOccurredAtUtc =
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
        var deadLetterOccurredAtUtc = firstOccurredAtUtc.AddMinutes(-2);
        var secondOccurredAtUtc = firstOccurredAtUtc.AddMinutes(2);
        var clock = new MutableClock(deadLetterOccurredAtUtc);
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        await using var services = BuildServices(configuration, clock);
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("dead-letter"));
        await using (var deadLetterScope = services.CreateAsyncScope())
        {
            deadLetterScope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>()
                .SetHost();
            var deadLetterStore =
                deadLetterScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var deadLetterLease = await deadLetterStore.AcquireAsync(
                1,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            Assert.HasCount(1, deadLetterLease);
            await deadLetterStore.MarkDeadLetterAsync(
                deadLetterLease[0].Id,
                deadLetterLease[0].LockId,
                "test dead letter",
                OutboxDeadLetterReasons.HandlerNotFound,
                deadLetterOccurredAtUtc,
                CancellationToken.None);
        }

        clock.UtcNow = firstOccurredAtUtc;
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("first"));
        clock.UtcNow = secondOccurredAtUtc;
        await InsertOutboxAsync(
            services,
            UnknownVersionHandler.EventTypeValue,
            1,
            new TestIntegrationEvent("second"));
        await UpdateOutboxNextAttemptAsync(
            databaseProvider,
            connectionString,
            secondOccurredAtUtc,
            secondOccurredAtUtc);

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var backlogReader =
            scope.ServiceProvider.GetRequiredService<IOutboxBacklogReader>();
        var initial = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(2L, initial.PendingCount);
        Assert.AreEqual(firstOccurredAtUtc, initial.OldestOccurredAtUtc);
        Assert.AreEqual(1L, initial.DueRetryCount);
        Assert.AreEqual(0L, initial.ActiveLeaseCount);
        Assert.AreEqual(1L, initial.DeadLetterCount);
        Assert.AreEqual(
            deadLetterOccurredAtUtc,
            initial.OldestDeadLetteredAtUtc);

        var firstLease = await store.AcquireAsync(
            1,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.HasCount(1, firstLease);
        var duringFirstLease = await backlogReader.ReadBacklogAsync(
            CancellationToken.None);
        Assert.AreEqual(1L, duringFirstLease.ActiveLeaseCount);
        Assert.AreEqual(1L, duringFirstLease.DueRetryCount);
        await store.MarkProcessedAsync(
            firstLease[0].Id,
            firstLease[0].LockId,
            CancellationToken.None);
        var afterFirst = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(1L, afterFirst.PendingCount);
        Assert.AreEqual(secondOccurredAtUtc, afterFirst.OldestOccurredAtUtc);
        Assert.AreEqual(1L, afterFirst.DueRetryCount);
        Assert.AreEqual(0L, afterFirst.ActiveLeaseCount);

        var secondLease = await store.AcquireAsync(
            1,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.HasCount(1, secondLease);
        var duringSecondLease = await backlogReader.ReadBacklogAsync(
            CancellationToken.None);
        Assert.AreEqual(0L, duringSecondLease.DueRetryCount);
        Assert.AreEqual(1L, duringSecondLease.ActiveLeaseCount);
        await store.MarkProcessedAsync(
            secondLease[0].Id,
            secondLease[0].LockId,
            CancellationToken.None);
        var empty = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(0L, empty.PendingCount);
        Assert.IsNull(empty.OldestOccurredAtUtc);
        Assert.AreEqual(0L, empty.DueRetryCount);
        Assert.AreEqual(0L, empty.ActiveLeaseCount);
        Assert.AreEqual(1L, empty.DeadLetterCount);
        Assert.AreEqual(
            deadLetterOccurredAtUtc,
            empty.OldestDeadLetteredAtUtc);
    }

    private static async Task VerifyRetentionAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        const string expired = "fullnet.tests.messaging.retention.expired";
        const string atCutoff = "fullnet.tests.messaging.retention.at_cutoff";
        const string fresh = "fullnet.tests.messaging.retention.fresh";
        const string retry = "fullnet.tests.messaging.retention.retry";
        const string leased = "fullnet.tests.messaging.retention.leased";
        const string deadLetter = "fullnet.tests.messaging.retention.dead_letter";
        var cutoff =
            new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(cutoff.AddDays(30));
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        await using var services = BuildServices(configuration, clock);
        foreach (var messageType in new[]
                 {
                     expired,
                     atCutoff,
                     fresh,
                     retry,
                     leased,
                     deadLetter,
                 })
        {
            await InsertOutboxAsync(
                services,
                messageType,
                1,
                new TestIntegrationEvent(messageType));
        }

        await SetRetentionStatesAsync(
            databaseProvider,
            connectionString,
            expired,
            atCutoff,
            fresh,
            retry,
            leased,
            deadLetter,
            cutoff);

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var retentionStore = scope.ServiceProvider
            .GetRequiredService<IOutboxRetentionStore>();

        var firstBatch = await retentionStore.DeleteProcessedBatchAsync(
            cutoff,
            1,
            CancellationToken.None);
        var secondBatch = await retentionStore.DeleteProcessedBatchAsync(
            cutoff,
            1,
            CancellationToken.None);

        Assert.AreEqual(1, firstBatch);
        Assert.AreEqual(0, secondBatch);
        Assert.AreEqual(
            0L,
            await CountOutboxAsync(
                databaseProvider,
                connectionString,
                "MessageType = @MessageType",
                new { MessageType = expired }));
        foreach (var retainedType in new[]
                 {
                     atCutoff,
                     fresh,
                     retry,
                     leased,
                     deadLetter,
                 })
        {
            Assert.AreEqual(
                1L,
                await CountOutboxAsync(
                    databaseProvider,
                    connectionString,
                    "MessageType = @MessageType",
                    new { MessageType = retainedType }),
                retainedType);
        }
    }

    private static async Task VerifyVersionRetirementSnapshotAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        const string canonicalType =
            "fullnet.tests.messaging.version_retirement.current";
        const string legacyType =
            "fullnet.tests.messaging.version-retirement.legacy";
        const string processedAlias =
            "fullnet.tests.messaging.version-retirement.processed";
        const string otherType =
            "fullnet.tests.messaging.version_retirement.other";
        var deadLetterOccurredAtUtc =
            new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(deadLetterOccurredAtUtc);
        var configuration = CreateConfiguration(databaseProvider, connectionString);
        await MigrateAsync(databaseProvider, connectionString);

        await using var services = BuildServices(configuration, clock);
        await InsertOutboxAsync(
            services,
            legacyType,
            1,
            new TestIntegrationEvent("dead-letter"));
        await UpdateOutboxTimestampAsync(
            databaseProvider,
            connectionString,
            legacyType,
            "DeadLetteredAtUtc",
            deadLetterOccurredAtUtc);

        clock.UtcNow = deadLetterOccurredAtUtc.AddMinutes(1);
        await InsertOutboxAsync(
            services,
            canonicalType,
            1,
            new TestIntegrationEvent("pending"));

        clock.UtcNow = deadLetterOccurredAtUtc.AddMinutes(2);
        await InsertOutboxAsync(
            services,
            processedAlias,
            1,
            new TestIntegrationEvent("processed"));
        await UpdateOutboxTimestampAsync(
            databaseProvider,
            connectionString,
            processedAlias,
            "ProcessedAtUtc",
            clock.UtcNow);

        clock.UtcNow = deadLetterOccurredAtUtc.AddMinutes(3);
        await InsertOutboxAsync(
            services,
            otherType,
            1,
            new TestIntegrationEvent("other-type"));
        await InsertOutboxAsync(
            services,
            canonicalType,
            2,
            new TestIntegrationEvent("other-version"));

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var reader = scope.ServiceProvider.GetRequiredService<IOutboxBacklogReader>();
        var snapshot = await reader.ReadVersionRetirementAsync(
            [canonicalType, legacyType, processedAlias],
            1,
            CancellationToken.None);

        Assert.AreEqual(1L, snapshot.PendingCount);
        Assert.AreEqual(1L, snapshot.DeadLetterCount);
        Assert.AreEqual(
            deadLetterOccurredAtUtc,
            snapshot.OldestUnprocessedOccurredAtUtc);
    }

    private static WorkerHost.OutboxProcessor CreateProcessor(
        ServiceProvider services,
        MutableClock clock,
        WorkerHost.OutboxWorkerOptions? options = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        clock,
        Options.Create(options ?? new WorkerHost.OutboxWorkerOptions
        {
            BatchSize = 20,
            LeaseSeconds = 30,
            PollMilliseconds = 1000,
            MaxAttempts = 3,
        }),
        NullLogger<WorkerHost.OutboxProcessor>.Instance);

    private static IConfiguration CreateConfiguration(
        DatabaseProvider databaseProvider,
        string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = databaseProvider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            })
            .Build();

    private static ServiceProvider BuildServices(
        IConfiguration configuration,
        MutableClock clock,
        params IIntegrationEventHandler[] handlers) =>
        BuildServices(configuration, clock, _ => { }, handlers);

    private static ServiceProvider BuildServices(
        IConfiguration configuration,
        MutableClock clock,
        Action<IServiceCollection> configureServices,
        params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        foreach (var handler in handlers)
        {
            services.AddSingleton<IIntegrationEventHandler>(handler);
        }
        configureServices(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static async Task MigrateAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var options = new DatabaseOptions
        {
            Provider = databaseProvider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        var runner = new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
        var result = await runner.MigrateAsync();
        Assert.IsTrue(result.Successful);
    }

    private static async Task InsertOutboxAsync(
        ServiceProvider services,
        string eventType,
        int schemaVersion,
        TestIntegrationEvent payload)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        await transaction.ExecuteAsync(
            async cancellationToken =>
            {
                await writer.AddAsync(eventType, schemaVersion, payload, cancellationToken);
                return 0;
            },
            CancellationToken.None);
    }

    private static async Task OverwritePayloadAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        string messageType,
        int schemaVersion,
        byte[] payload)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        await connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET Payload = @Payload
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            """,
            new
            {
                Payload = payload,
                MessageType = messageType,
                SchemaVersion = schemaVersion
            });
    }

    private static async Task UpdateOutboxTimestampAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        string messageType,
        string columnName,
        DateTimeOffset timestamp)
    {
        Assert.IsTrue(
            columnName is "ProcessedAtUtc" or "DeadLetteredAtUtc",
            "????????????? Outbox ??????");
        var databaseTimestamp = databaseProvider == DatabaseProvider.MySql
            ? (object)timestamp.UtcDateTime
            : timestamp;
        await using var connection = CreateConnection(
            databaseProvider,
            connectionString);
        await connection.ExecuteAsync(
            $"""
             UPDATE fn_outbox_message
             SET {columnName} = @Timestamp
             WHERE MessageType = @MessageType
             """,
            new
            {
                Timestamp = databaseTimestamp,
                MessageType = messageType
            });
    }

    private static async Task UpdateOutboxNextAttemptAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset nextAttemptAtUtc)
    {
        static object ToDatabaseTimestamp(
            DatabaseProvider provider,
            DateTimeOffset value) =>
            provider == DatabaseProvider.MySql
                ? value.UtcDateTime
                : value;

        await using var connection = CreateConnection(
            databaseProvider,
            connectionString);
        await connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET NextAttemptAtUtc = @NextAttemptAtUtc
            WHERE OccurredAtUtc = @OccurredAtUtc;
            """,
            new
            {
                OccurredAtUtc = ToDatabaseTimestamp(
                    databaseProvider,
                    occurredAtUtc),
                NextAttemptAtUtc = ToDatabaseTimestamp(
                    databaseProvider,
                    nextAttemptAtUtc),
            });
    }

    private static async Task SetRetentionStatesAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        string expired,
        string atCutoff,
        string fresh,
        string retry,
        string leased,
        string deadLetter,
        DateTimeOffset cutoff)
    {
        static object ToDatabaseTimestamp(
            DatabaseProvider provider,
            DateTimeOffset value) =>
            provider == DatabaseProvider.MySql
                ? value.UtcDateTime
                : value;

        await using var connection = CreateConnection(
            databaseProvider,
            connectionString);
        await connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET ProcessedAtUtc = CASE MessageType
                    WHEN @Expired THEN @ExpiredAt
                    WHEN @AtCutoff THEN @Cutoff
                    WHEN @Fresh THEN @FreshAt
                    ELSE ProcessedAtUtc
                END,
                NextAttemptAtUtc = CASE
                    WHEN MessageType = @Retry THEN @RetryAt
                    ELSE NextAttemptAtUtc
                END,
                LockId = CASE
                    WHEN MessageType = @Leased THEN @LockId
                    ELSE LockId
                END,
                LockedUntilUtc = CASE
                    WHEN MessageType = @Leased THEN @LockedUntil
                    ELSE LockedUntilUtc
                END,
                DeadLetteredAtUtc = CASE
                    WHEN MessageType = @DeadLetter THEN @DeadLetteredAt
                    ELSE DeadLetteredAtUtc
                END,
                DeadLetterReasonCode = CASE
                    WHEN MessageType = @DeadLetter THEN @DeadLetterReasonCode
                    ELSE DeadLetterReasonCode
                END
            WHERE MessageType IN (
                @Expired,
                @AtCutoff,
                @Fresh,
                @Retry,
                @Leased,
                @DeadLetter
            );
            """,
            new
            {
                Expired = expired,
                AtCutoff = atCutoff,
                Fresh = fresh,
                Retry = retry,
                Leased = leased,
                DeadLetter = deadLetter,
                ExpiredAt = ToDatabaseTimestamp(
                    databaseProvider,
                    cutoff.AddTicks(-1)),
                Cutoff = ToDatabaseTimestamp(databaseProvider, cutoff),
                FreshAt = ToDatabaseTimestamp(
                    databaseProvider,
                    cutoff.AddTicks(1)),
                RetryAt = ToDatabaseTimestamp(
                    databaseProvider,
                    cutoff.AddDays(1)),
                LockId = Guid.CreateVersion7(),
                LockedUntil = ToDatabaseTimestamp(
                    databaseProvider,
                    cutoff.AddDays(1)),
                DeadLetteredAt = ToDatabaseTimestamp(
                    databaseProvider,
                    cutoff.AddDays(-1)),
                DeadLetterReasonCode = OutboxDeadLetterReasons.HandlerNotFound,
            });
    }

    private static async Task<long> CountOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        string predicate,
        object? parameters = null)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM fn_outbox_message WHERE {predicate}",
            parameters);
    }

    private static async Task<int> ReadAttemptsAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT Attempts FROM fn_outbox_message");
    }

    private static DbConnection CreateConnection(
        DatabaseProvider databaseProvider,
        string connectionString) => databaseProvider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseProvider)),
        };

    [MemoryPackable]
    internal partial record TestIntegrationEvent(string Value);

    private sealed class RecordingHandler : IIntegrationEventHandler
    {
        public string EventType => UnknownVersionHandler.EventTypeValue;

        public int SchemaVersion => 1;

        public int HandledCount { get; private set; }

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DeserializingHandler(IIntegrationEventSerializer serializer)
        : IIntegrationEventHandler
    {
        public const string EventTypeValue = "fullnet.tests.messaging.invalid_payload";

        public string EventType => EventTypeValue;

        public int SchemaVersion => 1;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            serializer.Deserialize<TestIntegrationEvent>(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class UnknownVersionHandler : IIntegrationEventHandler
    {
        public const string EventTypeValue = "fullnet.tests.messaging.unknown_version";

        public string EventType => EventTypeValue;

        public int SchemaVersion => 1;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class BatchTailLeaseHandler : IIntegrationEventHandler
    {
        public const string EventTypeValue =
            "fullnet.tests.messaging.batch_tail_lease";

        private readonly TaskCompletionSource _firstMessageEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstMessage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _handledCount;

        public string EventType => EventTypeValue;

        public int SchemaVersion => 1;

        public Task FirstMessageEntered => _firstMessageEntered.Task;

        public int HandledCount => Volatile.Read(ref _handledCount);

        public async Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            var handledCount = Interlocked.Increment(ref _handledCount);
            if (handledCount != 1)
            {
                return;
            }

            _firstMessageEntered.TrySetResult();
            await _releaseFirstMessage.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseFirstMessage() =>
            _releaseFirstMessage.TrySetResult();
    }

    private sealed class CoordinatedConcurrencyHandler : IIntegrationEventHandler
    {
        public const string EventTypeValue =
            "fullnet.tests.messaging.bounded_concurrency";

        private readonly OutboxConcurrencyProbe _probe;

        public CoordinatedConcurrencyHandler(OutboxConcurrencyProbe probe)
        {
            _probe = probe;
            _probe.RegisterHandlerInstance();
        }

        public string EventType => EventTypeValue;

        public int SchemaVersion => 1;

        public async Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await _probe.WaitForReleaseAsync(cancellationToken);
                _probe.RecordHandled();
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class OutboxConcurrencyProbe(int expectedConcurrency)
    {
        private readonly TaskCompletionSource _concurrencyReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCount;
        private int _handledCount;
        private int _handlerInstanceCount;
        private int _peakConcurrency;

        public Task ConcurrencyReached => _concurrencyReached.Task;

        public int HandledCount => Volatile.Read(ref _handledCount);

        public int HandlerInstanceCount =>
            Volatile.Read(ref _handlerInstanceCount);

        public int PeakConcurrency => Volatile.Read(ref _peakConcurrency);

        public void Enter()
        {
            var activeCount = Interlocked.Increment(ref _activeCount);
            UpdatePeak(activeCount);
            if (activeCount >= expectedConcurrency)
            {
                _concurrencyReached.TrySetResult();
            }
        }

        public void Exit() => Interlocked.Decrement(ref _activeCount);

        public void RecordHandled() => Interlocked.Increment(ref _handledCount);

        public void RegisterHandlerInstance() =>
            Interlocked.Increment(ref _handlerInstanceCount);

        public void Release() => _release.TrySetResult();

        public Task WaitForReleaseAsync(CancellationToken cancellationToken) =>
            _release.Task.WaitAsync(cancellationToken);

        private void UpdatePeak(int activeCount)
        {
            while (true)
            {
                var peak = Volatile.Read(ref _peakConcurrency);
                if (activeCount <= peak
                    || Interlocked.CompareExchange(
                        ref _peakConcurrency,
                        activeCount,
                        peak) == peak)
                {
                    return;
                }
            }
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed record ActiveLeaseRow(
        Guid Id,
        Guid LockId,
        DateTimeOffset LockedUntilUtc);
}
