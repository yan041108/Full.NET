using System.Security.Cryptography;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Inbox;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class DapperIntegrationEventInboxTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "messaging.inbox.precheck_batch.sql_server")]
    [DataRow(DatabaseProvider.MySql, "messaging.inbox.precheck_batch.my_sql")]
    public async Task PrecheckBatch_classifies_unknown_processed_and_payload_mismatch_in_one_roundtrip(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var unknown = Guid.CreateVersion7();
        var processed = Guid.CreateVersion7();
        var mismatch = Guid.CreateVersion7();
        var processedHash = SHA256.HashData([0x02]);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<InboxBatchPrecheckRow>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new InboxBatchPrecheckRow(0, null, null),
                new InboxBatchPrecheckRow(1, InboxSql.StatusProcessed, processedHash),
                new InboxBatchPrecheckRow(2, InboxSql.StatusProcessed, new byte[32]),
            ]);
        var inbox = CreateInbox(provider, query, Substitute.For<ICommandExecutor>());

        var results = await inbox.PrecheckBatchAsync(
            "fullnet.messaging.inbox.test",
            [
                new InboxMessageFingerprint(unknown, SHA256.HashData([0x01])),
                new InboxMessageFingerprint(processed, processedHash),
                new InboxMessageFingerprint(mismatch, SHA256.HashData([0x03])),
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
        await query.Received(1).QueryAsync<InboxBatchPrecheckRow>(
            Arg.Is<SqlStatement>(statement => statement != null && statement.Name == expectedStatementName),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PrecheckBatch_rejects_duplicate_message_ids_before_database_access()
    {
        var messageId = Guid.CreateVersion7();
        var query = Substitute.For<IQueryExecutor>();
        var inbox = CreateInbox(
            DatabaseProvider.SqlServer,
            query,
            Substitute.For<ICommandExecutor>());

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => inbox.PrecheckBatchAsync(
            "fullnet.messaging.inbox.test",
            [
                new InboxMessageFingerprint(messageId, SHA256.HashData([0x01])),
                new InboxMessageFingerprint(messageId, SHA256.HashData([0x01])),
            ],
            CancellationToken.None));

        await query.DidNotReceiveWithAnyArgs().QueryAsync<InboxBatchPrecheckRow>(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task PrecheckBatch_rejects_more_than_one_hundred_messages()
    {
        var inbox = CreateInbox(
            DatabaseProvider.MySql,
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>());
        var messages = Enumerable.Range(0, 101)
            .Select(index => new InboxMessageFingerprint(Guid.CreateVersion7(), SHA256.HashData(BitConverter.GetBytes(index))))
            .ToArray();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => inbox.PrecheckBatchAsync(
            "fullnet.messaging.inbox.test",
            messages,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "messaging.inbox.claim.sql_server")]
    [DataRow(DatabaseProvider.MySql, "messaging.inbox.claim.my_sql")]
    public async Task Claim_uses_one_database_roundtrip_for_new_delivery(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var envelope = CreateEnvelope([1, 2, 3]);
        var payloadHash = SHA256.HashData(envelope.Payload.Span);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<InboxClaimRow>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxClaimRow(InboxSql.StatusProcessing, payloadHash));
        var command = Substitute.For<ICommandExecutor>();
        var inbox = CreateInbox(provider, query, command);

        var result = await inbox.ClaimAsync(
            "fullnet.messaging.inbox.test",
            envelope,
            CancellationToken.None);

        Assert.AreEqual(InboxClaimStatus.Claimed, result.Status);
        await query.Received(1).QuerySingleOrDefaultAsync<InboxClaimRow>(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == expectedStatementName),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task Claim_processed_duplicate_returns_without_write_roundtrip()
    {
        var envelope = CreateEnvelope([9]);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<InboxClaimRow>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxClaimRow(
                InboxSql.StatusProcessed,
                SHA256.HashData(envelope.Payload.Span)));
        var command = Substitute.For<ICommandExecutor>();
        var inbox = CreateInbox(DatabaseProvider.SqlServer, query, command);

        var result = await inbox.ClaimAsync(
            "fullnet.messaging.inbox.test",
            envelope,
            CancellationToken.None);

        Assert.AreEqual(InboxClaimStatus.AlreadyProcessed, result.Status);
        await query.Received(1).QuerySingleOrDefaultAsync<InboxClaimRow>(
            Arg.Any<SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    private static DapperIntegrationEventInbox CreateInbox(
        DatabaseProvider provider,
        IQueryExecutor query,
        ICommandExecutor command)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new DapperIntegrationEventInbox(
            query,
            command,
            clock,
            Options.Create(new DatabaseOptions { Provider = provider }));
    }

    private static IntegrationEventEnvelope CreateEnvelope(byte[] payload) =>
        IntegrationEventEnvelope.Create(
            Guid.CreateVersion7(),
            "fullnet.messaging.inbox.test.event",
            1,
            MessagingNames.ContentTypeMessagePack,
            null,
            "aggregate-1",
            "inbox-unit-test",
            null,
            null,
            "fullnet.messaging.tests",
            DateTimeOffset.UtcNow,
            payload);
}
