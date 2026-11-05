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

/// <summary>正式双库验证未知退款只能恢复单一原意图，参数冲突不领取、不改版本、不调用渠道。</summary>
[TestClass]
public sealed class PaymentRefundRecoveryMatchApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Unknown_refund_recovery_rejects_changed_intent_and_preserves_original_provider_identity(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new RefundChannel();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme").Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>(); var now = DateTimeOffset.UtcNow;
        var merchant = Guid.CreateVersion7(); var order = Guid.CreateVersion7();
        await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, P(("Id", merchant), ("TenantId", tenant), ("Name", "refund-recovery"),
            ("ChannelKey", "wechat_native"), ("AppId", "test"), ("MerchantId", "test"), ("CertificateSerialNo", "test"),
            ("NotifyUrl", "https://example.com/notify"), ("ReturnUrl", null), ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"),
            ("IsDefault", false), ("IsEnabled", true), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)));
        await commands.ExecuteAsync(PaymentOrderSql.Insert, P(("Id", order), ("TenantId", tenant), ("MerchantConfigId", merchant),
            ("ChannelKey", "wechat_native"), ("OutTradeNo", order.ToString("N")), ("TradeStateKey", "succeeded"), ("AmountMinor", 100L),
            ("Currency", "CNY"), ("Subject", "original"), ("Description", null), ("CodeUrl", null), ("ProviderTransactionId", "original-payment"),
            ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("PaidAtUtc", now), ("Version", 1),
            ("IdempotencyKey", Guid.CreateVersion7()), ("RequestHash", new string('A', 64))));
        var path = $"/api/v1/payments/orders/{order:D}/refunds";
        using var first = await host.PostAsJsonAsync(path, new CreatePaymentRefundRequest(29, "original"));
        Assert.AreEqual(HttpStatusCode.Conflict, first.StatusCode);
        Assert.AreEqual(PaymentErrorCodes.RefundProviderUnknown, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var original = (await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QueryAsync<PaymentRefundRecord>(
            PaymentRefundSql.ListRecoverableByOrderId, P(("OrderId", order)))).Single();
        var orderVersion = await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = order });
        var conflictTitle = "本次退款参数与原退款意图不一致，请核对原退款记录。";
        foreach (var request in new[] { new CreatePaymentRefundRequest(30, "original"), new(29, "changed"), new(null, "original") })
            await AssertConflictAsync(request);
        host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        conflictTitle = "The refund parameters do not match the original refund intent. Check the original refund record.";
        await AssertConflictAsync(new(30, "original"));
        host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        conflictTitle = "本次退款参数与原退款意图不一致，请核对原退款记录。";
        // 持久化所有者元数据损坏也不能把原意图带入其他商户或租户。
        foreach (var field in new[] { "TenantId", "MerchantConfigId", "Currency" })
        {
            if (field == "Currency") await database.ExecuteAsync("UPDATE fn_payment_refund SET Currency='USD' WHERE Id=@Id", new { Id = original.Id });
            else if (field == "TenantId") await database.ExecuteAsync("UPDATE fn_payment_refund SET TenantId=@Other WHERE Id=@Id", new { Id = original.Id, Other = Guid.CreateVersion7() });
            else await database.ExecuteAsync("UPDATE fn_payment_refund SET MerchantConfigId=@Other WHERE Id=@Id", new { Id = original.Id, Other = Guid.CreateVersion7() });
            await AssertConflictAsync(new(29, "original"));
            await database.ExecuteAsync("UPDATE fn_payment_refund SET TenantId=@TenantId,MerchantConfigId=@MerchantConfigId,Currency=@Currency WHERE Id=@Id", original);
        }
        var duplicate = Guid.CreateVersion7();
        await commands.ExecuteAsync(PaymentRefundSql.Insert, P(("Id", duplicate), ("TenantId", tenant), ("OrderId", order),
            ("MerchantConfigId", merchant), ("OutTradeNo", original.OutTradeNo), ("OutRefundNo", "duplicate-" + duplicate.ToString("N")),
            ("RefundStateKey", "provider_unknown"), ("AmountMinor", 29L), ("Currency", "CNY"), ("Reason", "original"),
            ("ProviderRefundId", null), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("CompletedAtUtc", null), ("Version", 1)));
        await AssertConflictAsync(new(29, "original"));
        await database.ExecuteAsync("DELETE FROM fn_payment_refund WHERE Id=@Id", new { Id = duplicate });
        Assert.AreEqual(orderVersion, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = order }));
        channel.Unknown = false;
        using var recovered = await host.PostAsJsonAsync(path, new CreatePaymentRefundRequest(29, " original "));
        Assert.AreEqual(HttpStatusCode.Created, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
        var receipt = await recovered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(original.Id, receipt.GetProperty("id").GetGuid()); Assert.AreEqual(original.OutRefundNo, receipt.GetProperty("outRefundNo").GetString());
        Assert.AreEqual("29", receipt.GetProperty("amountMinor").GetString());
        Assert.AreEqual(2, channel.OutRefundNos.Count); Assert.AreEqual(channel.OutRefundNos[0], channel.OutRefundNos[1]);
        Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_refund WHERE OrderId=@Id", new { Id = order }));

        async Task AssertConflictAsync(CreatePaymentRefundRequest body)
        {
            using var response = await host.PostAsJsonAsync(path, body);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual(PaymentErrorCodes.RefundRequestConflict, problem.GetProperty("code").GetString());
            Assert.AreEqual(conflictTitle, problem.GetProperty("title").GetString());
            Assert.AreEqual(1, channel.OutRefundNos.Count);
            Assert.AreEqual(original.Version, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_refund WHERE Id=@Id", new { Id = original.Id }));
        }
    }

    private static Dictionary<string, object?> P(params (string Key, object? Value)[] items) => items.ToDictionary(item => item.Key, item => item.Value);

    private sealed class RefundChannel : IWeChatNativePayClient
    {
        internal bool Unknown = true;
        internal List<string> OutRefundNos { get; } = [];
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(29L, amountMinor); Assert.AreEqual(100L, totalMinor); Assert.AreEqual("CNY", currency); Assert.AreEqual("original", reason);
            OutRefundNos.Add(outRefundNo); if (Unknown) throw new HttpRequestException("provider response unknown");
            return Task.FromResult(WeChatRefundResult.Success("original-provider-refund", "PROCESSING"));
        }
    }
}
