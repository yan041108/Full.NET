using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>双库 HTTP 对账在调用渠道前拒绝商户归属漂移，正确两种渠道仍可推进本地状态。</summary>
[TestClass]
public sealed class PaymentOrderReconciliationBoundaryApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Reconciliation_rejects_drift_and_preserves_original_intent(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channels = new ControlledChannels();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channels)); services.Replace(ServiceDescriptor.Singleton<IAlipayPagePayClient>(channels)); });
        await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme").Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>(); var now = DateTimeOffset.UtcNow;
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        foreach (var channel in new[] { "wechat_native", "alipay_page" })
        {
            var merchant = Guid.CreateVersion7(); var order = Guid.CreateVersion7();
            await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, P(("Id", merchant), ("TenantId", tenant), ("Name", "reconcile-owner"),
                ("ChannelKey", channel), ("AppId", "test"), ("MerchantId", "test"), ("CertificateSerialNo", "test"), ("NotifyUrl", "https://example.com/notify"),
                ("ReturnUrl", null), ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"), ("IsDefault", false), ("IsEnabled", true), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)));
            await commands.ExecuteAsync(PaymentOrderSql.Insert, P(("Id", order), ("TenantId", tenant), ("MerchantConfigId", merchant), ("ChannelKey", channel),
                ("OutTradeNo", order.ToString("N")), ("TradeStateKey", "provider_unknown"), ("AmountMinor", 100L), ("Currency", "CNY"), ("Subject", "original"),
                ("Description", null), ("CodeUrl", null), ("ProviderTransactionId", null), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("PaidAtUtc", null),
                ("Version", 1), ("IdempotencyKey", Guid.CreateVersion7()), ("RequestHash", new string('A', 64))));
            var calls = channels.Calls;
            foreach (var scenario in new[] { "tenant", "host", "channel" })
            {
                await database.ExecuteAsync("UPDATE fn_payment_merchant_config SET TenantId=@Tenant,ChannelKey=@Channel WHERE Id=@Id", new {
                    Tenant = scenario == "host" ? (Guid?)null : scenario == "tenant" ? Guid.CreateVersion7() : tenant,
                    Channel = scenario == "channel" ? (channel == "wechat_native" ? "alipay_page" : "wechat_native") : channel, Id = merchant });
                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
                    using var response = await host.PostAsync($"/api/v1/payments/orders/{order:D}/reconcile", null);
                    Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
                    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.AreEqual(PaymentErrorCodes.OrderMerchantMismatch, error.RootElement.GetProperty("code").GetString());
                    StringAssert.Contains(error.RootElement.GetProperty("title").GetString()!, language == "zh-CN" ? "原订单" : "original order");
                }
                Assert.AreEqual(calls, channels.Calls);
                Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
                Assert.AreEqual("provider_unknown", await database.ExecuteScalarAsync<string>("SELECT TradeStateKey FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
            }
            await database.ExecuteAsync("UPDATE fn_payment_merchant_config SET TenantId=@Tenant,ChannelKey=@Channel WHERE Id=@Id", new { Tenant = tenant, Channel = channel, Id = merchant });
            using var accepted = await host.PostAsync($"/api/v1/payments/orders/{order:D}/reconcile", null); accepted.EnsureSuccessStatusCode();
            var receipt = await accepted.Content.ReadFromJsonAsync<PaymentOrderResponse>(); Assert.AreEqual(order, receipt!.Id);
            Assert.AreEqual(2, receipt.Version); Assert.AreEqual("awaiting_payment", receipt.TradeStateKey); Assert.AreEqual(calls + 1, channels.Calls);
        }
    }
    private static IReadOnlyDictionary<string, object?> P(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    private sealed class ControlledChannels : IWeChatNativePayClient, IAlipayPagePayClient
    {
        internal int Calls { get; private set; }
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(WeChatTransactionQueryResult.Success("NOTPAY", null, 100)); }
        public Task<AlipayTradeQueryResult> QueryTradeByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(AlipayTradeQueryResult.Success("WAIT_BUYER_PAY", null, 100)); }
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Reconciliation cannot create payment orders.");
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Payment reconciliation cannot query refunds.");
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Reconciliation cannot create refunds.");
        public Task<AlipayPagePayResult> CreatePagePayUrlAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string subject, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Reconciliation cannot create payment orders.");
    }
}
