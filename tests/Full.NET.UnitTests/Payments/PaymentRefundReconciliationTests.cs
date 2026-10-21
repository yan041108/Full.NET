using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>退款查询绑定与双状态原子收敛；不能把异常退款释放为可重复退款的订单。</summary>
[TestClass]
public sealed class PaymentRefundReconciliationTests
{
    [TestMethod]
    [DataRow("SUCCESS", "succeeded", "refunded")]
    [DataRow("CLOSED", "closed", "succeeded")]
    [DataRow("PROCESSING", "processing", "refunding")]
    [DataRow("ABNORMAL", "failed", "refunding")]
    public async Task Reconcile_queries_outside_transaction_and_commits_both_states(string remote, string refundState, string orderState)
    {
        var fixture = Create(remote);
        var result = await fixture.Service.ReconcileAsync(fixture.Order, default);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.Coordinator.CommitCount);
        await fixture.Commands.Received(1).ExecuteAsync(PaymentRefundSql.UpdateProviderResult,
            Arg.Is<IReadOnlyDictionary<string, object?>>(p => (string)p!["RefundStateKey"]! == refundState), Arg.Any<CancellationToken>());
        await fixture.Commands.Received(1).ExecuteAsync(PaymentOrderSql.ClaimTradeState,
            Arg.Is<IReadOnlyDictionary<string, object?>>(p => (string)p!["TradeStateKey"]! == orderState), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Order_concurrency_conflict_rolls_back_refund_update()
    {
        var fixture = Create("SUCCESS");
        fixture.Commands.ExecuteAsync(PaymentOrderSql.ClaimTradeState, Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await fixture.Service.ReconcileAsync(fixture.Order, default)).IsSuccess);
        Assert.AreEqual(0, fixture.Coordinator.CommitCount);
        Assert.AreEqual(1, fixture.Coordinator.RollbackCount);
    }

    [TestMethod]
    [DataRow("UNKNOWN")]
    [DataRow("MISMATCH")]
    public async Task Invalid_snapshot_never_updates_either_record(string remote)
    {
        var fixture = Create(remote);
        Assert.IsFalse((await fixture.Service.ReconcileAsync(fixture.Order, default)).IsSuccess);
        await fixture.Commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    private static Fixture Create(string remote)
    {
        var merchant = new PaymentMerchantConfigRecord { Id = Guid.NewGuid(), IsEnabled = true };
        var order = new PaymentOrderRecord { Id = Guid.NewGuid(), MerchantConfigId = merchant.Id,
            TradeStateKey = PaymentTradeStateKeys.Refunding, Version = 2 };
        var refund = new PaymentRefundRecord { Id = Guid.NewGuid(), OrderId = order.Id, MerchantConfigId = merchant.Id,
            OutRefundNo = "refund-1", AmountMinor = 500, Currency = "CNY", Version = 1 };
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<PaymentRefundRecord>(PaymentRefundSql.ListUnsettledByOrderId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(new[] { refund });
        queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(PaymentMerchantConfigSql.FindById,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(merchant);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(1);
        var coordinator = new RecordingDbTransactionCoordinator();
        var client = Substitute.For<IWeChatNativePayClient>();
        client.QueryDomesticRefundAsync(merchant, "refund-1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsFalse(coordinator.HasTransaction);
            return Task.FromResult(new WeChatRefundQueryResult(true, "wx-1", "refund-1", remote,
                remote == "MISMATCH" ? 999 : 500, "CNY", null));
        });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new(new PaymentRefundReconciliationService(queries, commands, new DapperCommandTransaction(coordinator), client, clock),
            order, commands, coordinator);
    }

    private sealed record Fixture(PaymentRefundReconciliationService Service, PaymentOrderRecord Order,
        ICommandExecutor Commands, RecordingDbTransactionCoordinator Coordinator);
}
