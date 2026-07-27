extern alias workerhost;

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
using Full.NET.Serialization.MessagePack;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using global::MessagePack;
using WorkerHost = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class OutboxRecoveryTests
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
        await VerifyLeaseRecoveryAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_outbox_lease_prevents_duplicate_success_and_recovers_after_expiry()
    {
        await VerifyLeaseRecoveryAsync(
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
            new DeserializingHandler(new MessagePackIntegrationEventSerializer()));
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

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var backlogReader =
            scope.ServiceProvider.GetRequiredService<IOutboxBacklogReader>();
        var initial = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(2L, initial.PendingCount);
        Assert.AreEqual(firstOccurredAtUtc, initial.OldestOccurredAtUtc);

        var firstLease = await store.AcquireAsync(
            1,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.HasCount(1, firstLease);
        await store.MarkProcessedAsync(
            firstLease[0].Id,
            firstLease[0].LockId,
            CancellationToken.None);
        var afterFirst = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(1L, afterFirst.PendingCount);
        Assert.AreEqual(secondOccurredAtUtc, afterFirst.OldestOccurredAtUtc);

        var secondLease = await store.AcquireAsync(
            1,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.HasCount(1, secondLease);
        await store.MarkProcessedAsync(
            secondLease[0].Id,
            secondLease[0].LockId,
            CancellationToken.None);
        var empty = await backlogReader.ReadBacklogAsync(CancellationToken.None);
        Assert.AreEqual(0L, empty.PendingCount);
        Assert.IsNull(empty.OldestOccurredAtUtc);
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
        MutableClock clock) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        clock,
        Options.Create(new WorkerHost.OutboxWorkerOptions
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
        params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMessagePack();
        foreach (var handler in handlers)
        {
            services.AddSingleton<IIntegrationEventHandler>(handler);
        }

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
        await writer.AddAsync(eventType, schemaVersion, payload, CancellationToken.None);
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
            "测试夹具只允许更新已审核的 Outbox 终态时间列。");
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

    [MessagePackObject(AllowPrivate = true)]
    internal sealed record TestIntegrationEvent([property: Key(0)] string Value);

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

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
