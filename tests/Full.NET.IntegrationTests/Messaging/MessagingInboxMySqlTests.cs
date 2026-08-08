using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class MessagingInboxMySqlTests
{
    [TestMethod]
    public async Task MySql_inbox_first_processing_marks_processed_and_writes_downstream_outbox()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var downstreamPartitionKey = Guid.CreateVersion7().ToString("D");
        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x01, 0x02], eventId);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var subscription = new MessagingInboxTestSupport.DownstreamOutboxSubscription(
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>(),
            downstreamPartitionKey);
        var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);

        var result = await dispatcher.ConsumeAsync(
            MessagingInboxTestSupport.ConsumerName,
            envelope,
            subscription,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.Processed, result.Status);

        await using var connection = OpenMySql(connectionString);
        await MessagingInboxAssertions.AssertInboxStatusMySqlAsync(
            connection,
            MessagingInboxTestSupport.ConsumerName,
            eventId,
            "processed");
        await MessagingInboxAssertions.AssertDownstreamOutboxCountMySqlAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task MySql_inbox_duplicate_after_commit_returns_already_processed()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var downstreamPartitionKey = Guid.CreateVersion7().ToString("D");
        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x0A], eventId);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var subscription = new MessagingInboxTestSupport.DownstreamOutboxSubscription(
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>(),
            downstreamPartitionKey);
        var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);

        var first = await dispatcher.ConsumeAsync(
            MessagingInboxTestSupport.ConsumerName,
            envelope,
            subscription,
            CancellationToken.None);
        var second = await dispatcher.ConsumeAsync(
            MessagingInboxTestSupport.ConsumerName,
            envelope,
            subscription,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.Processed, first.Status);
        Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, second.Status);

        await using var connection = OpenMySql(connectionString);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountMySqlAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task MySql_inbox_handler_failure_rolls_back_claim_and_downstream_outbox()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var downstreamPartitionKey = Guid.CreateVersion7().ToString("D");
        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x11], eventId);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var subscription = new MessagingInboxTestSupport.ThrowingSubscription();
        var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            dispatcher.ConsumeAsync(
                MessagingInboxTestSupport.ConsumerName,
                envelope,
                subscription,
                CancellationToken.None));

        await using var connection = OpenMySql(connectionString);
        await MessagingInboxAssertions.AssertInboxCountMySqlAsync(
            connection,
            MessagingInboxTestSupport.ConsumerName,
            eventId,
            0);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountMySqlAsync(
            connection,
            downstreamPartitionKey,
            0);
    }

    [TestMethod]
    public async Task MySql_inbox_concurrent_duplicate_only_processes_once()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var downstreamPartitionKey = Guid.CreateVersion7().ToString("D");
        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x22], eventId);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);

        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var subscription = new MessagingInboxTestSupport.DownstreamOutboxSubscription(
                scope.ServiceProvider.GetRequiredService<IOutboxWriter>(),
                downstreamPartitionKey);
            var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);
            return await dispatcher.ConsumeAsync(
                MessagingInboxTestSupport.ConsumerName,
                envelope,
                subscription,
                CancellationToken.None);
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.AreEqual(1, results.Count(r => r.Status == InboxConsumeStatus.Processed));
        Assert.AreEqual(1, results.Count(r => r.Status == InboxConsumeStatus.AlreadyProcessed));

        await using var connection = OpenMySql(connectionString);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountMySqlAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task MySql_inbox_payload_mismatch_is_permanent_contract_failure()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x33], eventId);

        await using (var connection = OpenMySql(connectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                     PayloadHash, Status, Attempts, ReceivedAtUtc, ProcessedAtUtc)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL,
                     @PayloadHash, 'processed', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
                """,
                new
                {
                    ConsumerName = MessagingInboxTestSupport.ConsumerName,
                    MessageId = eventId,
                    MessageType = MessagingOutboxTestSupport.TestEventType,
                    SchemaVersion = MessagingOutboxTestSupport.TestSchemaVersion,
                    PayloadHash = new byte[32],
                });
        }

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var subscription = new MessagingInboxTestSupport.NoOpSubscription();
        var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);

        var exception = await Assert.ThrowsExactlyAsync<IntegrationEventPermanentException>(() =>
            dispatcher.ConsumeAsync(
                MessagingInboxTestSupport.ConsumerName,
                envelope,
                subscription,
                CancellationToken.None));

        Assert.AreEqual(
            IntegrationEventFailureCodes.MessageIdPayloadMismatch,
            exception.Failure.Code);
    }

    private static DatabaseOptions CreateOptions(string connectionString) => new()
    {
        Provider = DatabaseProvider.MySql,
        ConnectionString = connectionString,
        MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
        CommandTimeoutSeconds = 300,
    };

    private static MySqlConnection OpenMySql(string connectionString) =>
        new(MySqlConnectionStringPolicy.Create(
            connectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
}
