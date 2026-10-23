using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>单元测试（数据访问）：Dapper Event Stream Ownership Gate。</summary>
[TestClass]
public sealed class DapperEventStreamOwnershipGateTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, null)]
    [DataRow(DatabaseProvider.MySql, null)]
    [DataRow(DatabaseProvider.SqlServer, 0)]
    [DataRow(DatabaseProvider.MySql, 0)]
    [DataRow(DatabaseProvider.SqlServer, 1)]
    [DataRow(DatabaseProvider.MySql, 1)]
    [DataRow(DatabaseProvider.SqlServer, 2)]
    [DataRow(DatabaseProvider.MySql, 2)]
    [DataRow(DatabaseProvider.SqlServer, -1)]
    [DataRow(DatabaseProvider.MySql, -1)]
    [DataRow(DatabaseProvider.SqlServer, 99)]
    [DataRow(DatabaseProvider.MySql, 99)]
    public async Task Producer_returns_locked_owner_and_rejects_rollback_or_invalid_owner(DatabaseProvider provider, int? owner)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<int?>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(owner);
        var transaction = Substitute.For<IDbTransactionCoordinator>();
        transaction.HasTransaction.Returns(true);
        var gate = new DapperEventStreamOwnershipGate(queries, transaction, Options.Create(new DatabaseOptions { Provider = provider }));

        if (owner == -1)
            await Assert.ThrowsExactlyAsync<EventDeliveryProducerFencedException>(() => gate.AcquireProducerAsync("event", 1));
        else if (owner == 99)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gate.AcquireProducerAsync("event", 1));
        else
            Assert.AreEqual(owner.HasValue ? (EventDeliveryOwner?)owner.Value : null, await gate.AcquireProducerAsync("event", 1));
        Assert.AreEqual(1, queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Producer_without_transaction_does_not_read_ownership()
    {
        var queries = Substitute.For<IQueryExecutor>();
        var gate = new DapperEventStreamOwnershipGate(queries, Substitute.For<IDbTransactionCoordinator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gate.AcquireProducerAsync("event", 1));
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Consumer_fence_returns_owner_with_one_locked_query()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<int?>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns((int)EventDeliveryOwner.CdcKafka);
        var transaction = Substitute.For<IDbTransactionCoordinator>();
        transaction.HasTransaction.Returns(true);
        var gate = new DapperEventStreamOwnershipGate(
            queryExecutor,
            transaction,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await gate.AcquireConsumerFenceAsync(
            "fullnet.organization.unit.changed",
            1,
            CancellationToken.None);

        Assert.IsTrue(result.IsSupported);
        Assert.IsTrue(result.OwnershipExists);
        Assert.AreEqual(EventDeliveryOwner.CdcKafka, result.CurrentOwner);
        await queryExecutor.Received(1).QuerySingleOrDefaultAsync<int?>(
            Arg.Is<SqlStatement>(statement =>
                statement != null
                && statement.Name == "messaging.stream_ownership_gate.consumer.sql_server"),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }
}
