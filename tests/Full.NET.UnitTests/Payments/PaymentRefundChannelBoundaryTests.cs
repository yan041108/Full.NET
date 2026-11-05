using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>退款创建、未知意图恢复及对账必须在写入或渠道调用前拒绝错误渠道与商户归属。</summary>
[TestClass]
public sealed class PaymentRefundChannelBoundaryTests
{
    [TestMethod]
    [DataRow("alipay_page", "alipay_page", false)]
    [DataRow("unknown", "wechat_native", false)]
    [DataRow("", "wechat_native", false)]
    [DataRow("wechat_native", "alipay_page", false)]
    [DataRow("wechat_native", "wechat_native", true)]
    public async Task Rejected_channel_or_owner_never_writes_or_calls_provider(string channel, string merchantChannel, bool otherTenant)
    {
        var tenant = Guid.CreateVersion7(); var merchantId = Guid.CreateVersion7(); var orderId = Guid.CreateVersion7();
        var merchant = new PaymentMerchantConfigRecord { Id = merchantId, TenantId = otherTenant ? Guid.CreateVersion7() : tenant,
            ChannelKey = merchantChannel, IsEnabled = true };
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var client = Substitute.For<IWeChatNativePayClient>(); var clock = Substitute.For<IClock>();
        var coordinator = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(coordinator);
        var facts = new PaymentFinancialFactPublisher(Substitute.For<IOutboxWriter>());
        var create = new PaymentRefundManagementService(queries, commands, transaction,
            new PaymentRefundQueryService(queries, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })),
            client, clock, Substitute.For<IIdGenerator>(), facts);
        var reconcile = new PaymentRefundReconciliationService(queries, commands, transaction, client, clock, facts);
        queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(PaymentMerchantConfigSql.FindById,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(merchant);
        queries.QueryAsync<PaymentRefundRecord>(PaymentRefundSql.ListUnsettledByOrderId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(new[] {
                new PaymentRefundRecord { Id = Guid.CreateVersion7(), OrderId = orderId, TenantId = tenant,
                    MerchantConfigId = merchantId, OutRefundNo = "original", AmountMinor = 29, Currency = "CNY" } });
        var expected = channel == PaymentChannelKeys.WeChatNative ? PaymentErrorCodes.RefundMerchantMismatch : PaymentErrorCodes.RefundChannelUnsupported;
        foreach (var state in new[] { PaymentTradeStateKeys.Succeeded, PaymentTradeStateKeys.Refunding })
        {
            var order = new PaymentOrderRecord { Id = orderId, TenantId = tenant, MerchantConfigId = merchantId,
                ChannelKey = channel, TradeStateKey = state, AmountMinor = 100, Currency = "CNY", Version = 1 };
            queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindById,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(order);
            Assert.AreEqual(expected, (await create.CreateForOrderAsync(orderId, new(29, "original"))).Error!.Code);
            if (state == PaymentTradeStateKeys.Refunding)
                Assert.AreEqual(expected, (await reconcile.ReconcileAsync(order, default)).Error!.Code);
        }
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().CreateDomesticRefundAsync(Arg.Any<PaymentMerchantConfigRecord>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().QueryDomesticRefundAsync(Arg.Any<PaymentMerchantConfigRecord>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, coordinator.CommitCount);
    }
}
