using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>对账凭据必须匹配原资金归属，拒绝时不能查询渠道、写入或提交事务。</summary>
[TestClass]
public sealed class PaymentOrderReconciliationBoundaryTests
{
    [TestMethod]
    [DataRow("wechat_native", "id")] [DataRow("wechat_native", "tenant")]
    [DataRow("wechat_native", "host")] [DataRow("wechat_native", "channel")]
    [DataRow("alipay_page", "id")] [DataRow("alipay_page", "tenant")]
    [DataRow("alipay_page", "host")] [DataRow("alipay_page", "channel")]
    [DataRow("wechat_native", "valid")] [DataRow("alipay_page", "valid")]
    public async Task Reconciliation_checks_original_merchant_before_provider_query(string channel, string scenario)
    {
        var tenant = Guid.CreateVersion7(); var orderId = Guid.CreateVersion7(); var merchantId = Guid.CreateVersion7();
        var order = new PaymentOrderRecord { Id = orderId, TenantId = tenant, MerchantConfigId = merchantId,
            ChannelKey = channel, TradeStateKey = "provider_unknown", OutTradeNo = "original", AmountMinor = 100, Currency = "CNY", Version = 7 };
        var merchant = new PaymentMerchantConfigRecord { Id = scenario == "id" ? Guid.CreateVersion7() : merchantId,
            TenantId = scenario == "host" ? null : scenario == "tenant" ? Guid.CreateVersion7() : tenant,
            ChannelKey = scenario == "channel" ? (channel == "wechat_native" ? "alipay_page" : "wechat_native") : channel, IsEnabled = true };
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindById, Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(order);
        queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(PaymentMerchantConfigSql.FindById, Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns(merchant);
        var wechat = Substitute.For<IWeChatNativePayClient>(); var alipay = Substitute.For<IAlipayPagePayClient>();
        wechat.QueryTransactionByOutTradeNoAsync(merchant, "original", Arg.Any<CancellationToken>()).Returns(WeChatTransactionQueryResult.Failure("controlled failure"));
        alipay.QueryTradeByOutTradeNoAsync(merchant, "original", Arg.Any<CancellationToken>()).Returns(AlipayTradeQueryResult.Failure("controlled failure"));
        var coordinator = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(coordinator);
        var clock = Substitute.For<IClock>(); var facts = new PaymentFinancialFactPublisher(Substitute.For<IOutboxWriter>());
        var service = new PaymentOrderReconciliationService(queries, commands, transaction,
            new PaymentOrderQueryService(queries, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })),
            wechat, alipay, clock, new PaymentRefundReconciliationService(queries, commands, transaction, wechat, clock, facts), facts);
        var result = await service.ReconcileAsync(orderId);
        Assert.AreEqual(scenario == "valid" ? PaymentErrorCodes.OrderProviderFailed : PaymentErrorCodes.OrderMerchantMismatch, result.Error!.Code);
        await wechat.Received(scenario == "valid" && channel == "wechat_native" ? 1 : 0).QueryTransactionByOutTradeNoAsync(merchant, "original", Arg.Any<CancellationToken>());
        await alipay.Received(scenario == "valid" && channel == "alipay_page" ? 1 : 0).QueryTradeByOutTradeNoAsync(merchant, "original", Arg.Any<CancellationToken>());
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, coordinator.CommitCount); Assert.AreEqual(7, order.Version);
    }
}
