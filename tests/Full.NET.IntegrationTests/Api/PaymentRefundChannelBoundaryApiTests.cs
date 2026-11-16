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

/// <summary>正式 HTTP 边界拒绝未支持渠道与商户归属漂移，原意图、版本及渠道调用数保持不变。</summary>
[TestClass]
public sealed class PaymentRefundChannelBoundaryApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Refund_create_recovery_and_reconciliation_reject_wrong_channel_before_side_effects(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new ControlledRefundChannel();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme").Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>(); var now = DateTimeOffset.UtcNow;
        var merchant = Guid.CreateVersion7(); var order = Guid.CreateVersion7(); var refund = Guid.CreateVersion7();
        await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, P(("Id", merchant), ("TenantId", tenant), ("Name", "refund-channel"),
            ("ChannelKey", "wechat_native"), ("AppId", "test"), ("MerchantId", "test"), ("CertificateSerialNo", "test"),
            ("NotifyUrl", "https://example.com/notify"), ("ReturnUrl", null), ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"),
            ("IsDefault", false), ("IsEnabled", true), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)));
        await commands.ExecuteAsync(PaymentOrderSql.Insert, P(("Id", order), ("TenantId", tenant), ("MerchantConfigId", merchant),
            ("ChannelKey", "wechat_native"), ("OutTradeNo", order.ToString("N")), ("TradeStateKey", "succeeded"), ("AmountMinor", 100L),
            ("Currency", "CNY"), ("Subject", "original"), ("Description", null), ("CodeUrl", null), ("ProviderTransactionId", "paid"),
            ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("PaidAtUtc", now), ("Version", 1),
            ("IdempotencyKey", Guid.CreateVersion7()), ("RequestHash", new string('A', 64))));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        foreach (var state in new[] { "succeeded", "refunding" })
        {
            if (state == "refunding")
                await commands.ExecuteAsync(PaymentRefundSql.Insert, P(("Id", refund), ("TenantId", tenant), ("OrderId", order),
                    ("MerchantConfigId", merchant), ("OutTradeNo", order.ToString("N")), ("OutRefundNo", refund.ToString("N")),
                    ("RefundStateKey", "provider_unknown"), ("AmountMinor", 29L), ("Currency", "CNY"), ("Reason", "original"),
                    ("ProviderRefundId", null), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("CompletedAtUtc", null), ("Version", 1)));
            foreach (var scenario in new[] { "alipay", "merchant-channel", "merchant-tenant", "host-merchant" })
            {
                await database.ExecuteAsync("UPDATE fn_payment_order SET TradeStateKey=@State,ChannelKey=@Channel WHERE Id=@Id",
                    new { State = state, Channel = scenario == "alipay" ? "alipay_page" : "wechat_native", Id = order });
                await database.ExecuteAsync("UPDATE fn_payment_merchant_config SET ChannelKey=@Channel,TenantId=@Tenant WHERE Id=@Id",
                    new { Channel = scenario is "alipay" or "merchant-channel" ? "alipay_page" : "wechat_native",
                        Tenant = scenario == "host-merchant" ? (Guid?)null : scenario == "merchant-tenant" ? Guid.CreateVersion7() : tenant, Id = merchant });
                var unsupported = scenario == "alipay";
                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
                    using var response = await host.PostAsJsonAsync($"/api/v1/payments/orders/{order:D}/refunds", new CreatePaymentRefundRequest(29, "original"));
                    await AssertRejected(response, unsupported, language);
                    if (state == "refunding")
                    {
                        using var reconciled = await host.PostAsync($"/api/v1/payments/orders/{order:D}/reconcile", null);
                        await AssertRejected(reconciled, unsupported, language);
                    }
                }
                Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
                Assert.AreEqual(state, await database.ExecuteScalarAsync<string>("SELECT TradeStateKey FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
                Assert.AreEqual(state == "refunding" ? 1 : 0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_refund WHERE OrderId=@Id", new { Id = order }));
                if (state == "refunding")
                    Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_refund WHERE Id=@Id", new { Id = refund }));
                Assert.AreEqual(0, channel.Calls);
            }
        }

        await database.ExecuteAsync("UPDATE fn_payment_merchant_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = tenant, Id = merchant });
        var duplicate = Guid.CreateVersion7();
        await commands.ExecuteAsync(PaymentRefundSql.Insert, P(("Id", duplicate), ("TenantId", tenant), ("OrderId", order),
            ("MerchantConfigId", merchant), ("OutTradeNo", order.ToString("N")), ("OutRefundNo", duplicate.ToString("N")),
            ("RefundStateKey", "processing"), ("AmountMinor", 29L), ("Currency", "CNY"), ("Reason", "duplicate"),
            ("ProviderRefundId", null), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("CompletedAtUtc", null), ("Version", 1)));
        await AssertIntentConflictAsync(PaymentErrorCodes.RefundRequestConflict, 0);
        await database.ExecuteAsync("UPDATE fn_payment_refund SET RefundStateKey='failed' WHERE Id=@Id", new { Id = duplicate });

        var originalOutTradeNo = order.ToString("N");
        await SetIntentIdentityAsync(Guid.CreateVersion7(), originalOutTradeNo, "CNY");
        await AssertIntentConflictAsync(PaymentErrorCodes.RefundRequestConflict, 0);
        await SetIntentIdentityAsync(tenant, "other-trade", "CNY");
        await AssertIntentConflictAsync(PaymentErrorCodes.RefundRequestConflict, 0);
        await SetIntentIdentityAsync(tenant, originalOutTradeNo, "USD");
        await AssertIntentConflictAsync(PaymentErrorCodes.RefundRequestConflict, 0);
        await SetIntentIdentityAsync(tenant, originalOutTradeNo, "CNY");

        await database.ExecuteAsync("UPDATE fn_payment_refund SET RefundStateKey='failed', ProviderRefundId='original-provider-refund' WHERE Id=@Id", new { Id = refund });
        channel.ProviderRefundId = "other-provider-refund";
        await AssertIntentConflictAsync(PaymentErrorCodes.RefundProviderUnknown, 1);
        channel.ProviderRefundId = "original-provider-refund";
        using var accepted = await host.PostAsync($"/api/v1/payments/orders/{order:D}/reconcile", null);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
        Assert.AreEqual(2, channel.Calls);
        Assert.AreEqual(refund.ToString("N"), channel.LastOutRefundNo);
        Assert.AreEqual("refunded", await database.ExecuteScalarAsync<string>("SELECT TradeStateKey FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
        Assert.AreEqual("succeeded", await database.ExecuteScalarAsync<string>("SELECT RefundStateKey FROM fn_payment_refund WHERE Id=@Id", new { Id = refund }));
        Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_refund WHERE Id=@Id", new { Id = duplicate }));

        Task<int> SetIntentIdentityAsync(Guid owner, string outTradeNo, string currency) => database.ExecuteAsync(
            "UPDATE fn_payment_refund SET TenantId=@Owner,OutTradeNo=@OutTradeNo,Currency=@Currency WHERE Id=@Id",
            new { Owner = owner, OutTradeNo = outTradeNo, Currency = currency, Id = refund });

        async Task AssertIntentConflictAsync(string code, int calls)
        {
            using var response = await host.PostAsync($"/api/v1/payments/orders/{order:D}/reconcile", null);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual(code, problem.GetProperty("code").GetString());
            Assert.AreEqual(calls, channel.Calls);
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_refund WHERE Id=@Id", new { Id = refund }));
        }
    }

    private static async Task AssertRejected(HttpResponseMessage response, bool unsupported, string language)
    {
        Assert.AreEqual(unsupported ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Conflict, response.StatusCode,
            await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(unsupported ? PaymentErrorCodes.RefundChannelUnsupported : PaymentErrorCodes.RefundMerchantMismatch,
            problem.GetProperty("code").GetString());
        var title = unsupported ? language == "zh-CN" ? "当前仅支持微信 Native 订单退款及退款对账。"
            : "Refunds and refund reconciliation are currently supported only for WeChat Native orders."
            : language == "zh-CN" ? "当前商户配置与原订单的租户或渠道不一致，请核对原订单。"
            : "The merchant configuration does not match the original order tenant or channel. Check the original order.";
        Assert.AreEqual(title, problem.GetProperty("title").GetString());
    }

    private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value);

    private sealed class ControlledRefundChannel : IWeChatNativePayClient
    {
        internal int Calls;
        internal string ProviderRefundId { get; set; } = "original-provider-refund";
        internal string? LastOutRefundNo { get; private set; }
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOutRefundNo = outRefundNo;
            return Task.FromResult(new WeChatRefundQueryResult(true, ProviderRefundId, outRefundNo, "SUCCESS", 29, "CNY", null));
        }
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Rejected channel must never reach the provider."); }
    }
}
