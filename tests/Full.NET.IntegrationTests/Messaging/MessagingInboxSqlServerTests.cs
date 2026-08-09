using Dapper;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Messaging.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class MessagingInboxSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_inbox_batch_precheck_is_read_only_and_classifies_existing_hashes()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);
        var unknown = Guid.CreateVersion7();
        var processed = Guid.CreateVersion7();
        var mismatch = Guid.CreateVersion7();
        var processedHash = SHA256.HashData([0x41]);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                     PayloadHash, Status, Attempts, ReceivedAtUtc, ProcessedAtUtc)
                VALUES
                    (@ConsumerName, @Processed, @MessageType, 1, NULL,
                     @ProcessedHash, 'processed', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                    (@ConsumerName, @Mismatch, @MessageType, 1, NULL,
                     @MismatchHash, 'processed', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
                """,
                new
                {
                    ConsumerName = MessagingInboxTestSupport.ConsumerName,
                    Processed = processed,
                    Mismatch = mismatch,
                    MessageType = MessagingOutboxTestSupport.TestEventType,
                    ProcessedHash = processedHash,
                    MismatchHash = new byte[32],
                });
        }

        await using var services = MessagingInboxTestSupport.BuildInboxServices(
            MessagingOutboxTestSupport.CreateConfiguration(options));
        await using var scope = services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>();
        var results = await inbox.PrecheckBatchAsync(
            MessagingInboxTestSupport.ConsumerName,
            [
                new InboxMessageFingerprint(unknown, SHA256.HashData([0x40])),
                new InboxMessageFingerprint(processed, processedHash),
                new InboxMessageFingerprint(mismatch, SHA256.HashData([0x42])),
            ],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                InboxPrecheckStatus.Unknown,
                InboxPrecheckStatus.AlreadyProcessed,
                InboxPrecheckStatus.PayloadMismatch,
            },
            results.Select(result => result.Status).ToArray());
        await using var verify = new SqlConnection(connectionString);
        await MessagingInboxAssertions.AssertInboxCountSqlServerAsync(
            verify,
            MessagingInboxTestSupport.ConsumerName,
            unknown,
            0);
    }

    [TestMethod]
    public async Task SqlServer_inbox_batch_precheck_does_not_replace_transactional_claim()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        await MessagingInboxTestSupport.AssertPrecheckDoesNotOwnClaimAsync(
            MessagingOutboxTestSupport.CreateConfiguration(options));
    }

    [TestMethod]
    public async Task SqlServer_inbox_first_processing_marks_processed_and_writes_downstream_outbox()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
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

        await using var connection = new SqlConnection(connectionString);
        await MessagingInboxAssertions.AssertInboxStatusSqlServerAsync(
            connection,
            MessagingInboxTestSupport.ConsumerName,
            eventId,
            "processed");
        await MessagingInboxAssertions.AssertDownstreamOutboxCountSqlServerAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task SqlServer_inbox_duplicate_after_commit_returns_already_processed()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
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

        await using var connection = new SqlConnection(connectionString);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountSqlServerAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task SqlServer_inbox_handler_failure_rolls_back_claim_and_downstream_outbox()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
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

        await using var connection = new SqlConnection(connectionString);
        await MessagingInboxAssertions.AssertInboxCountSqlServerAsync(
            connection,
            MessagingInboxTestSupport.ConsumerName,
            eventId,
            0);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountSqlServerAsync(
            connection,
            downstreamPartitionKey,
            0);
    }

    [TestMethod]
    public async Task SqlServer_inbox_concurrent_duplicate_only_processes_once()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
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

        await using var connection = new SqlConnection(connectionString);
        await MessagingInboxAssertions.AssertDownstreamOutboxCountSqlServerAsync(
            connection,
            downstreamPartitionKey,
            1);
    }

    [TestMethod]
    public async Task SqlServer_inbox_payload_mismatch_is_permanent_contract_failure()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var options = CreateOptions(connectionString);
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var eventId = Guid.CreateVersion7();
        var envelope = MessagingInboxTestSupport.CreateEnvelope([0x33], eventId);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_messaging_inbox_message
                    (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                     PayloadHash, Status, Attempts, ReceivedAtUtc, ProcessedAtUtc)
                VALUES
                    (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, NULL,
                     @PayloadHash, 'processed', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
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
        Provider = DatabaseProvider.SqlServer,
        ConnectionString = connectionString,
        CommandTimeoutSeconds = 300,
    };
}
