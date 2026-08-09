using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DapperEventStreamOwnershipGateTests
{
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
