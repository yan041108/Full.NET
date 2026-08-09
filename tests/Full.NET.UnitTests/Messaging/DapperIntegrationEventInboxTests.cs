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
