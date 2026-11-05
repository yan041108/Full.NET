using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>正式双库 HTTP 验证完整金额字符串、非法数字零副作用及可空全额退款。</summary>
[TestClass]
public sealed class PaymentHttpWirePrecisionApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Payment_and_refund_http_keep_int64_strings_and_reject_numeric_body_before_writes(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new ExactAmountChannel();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme").Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>(); var merchant = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, P(("Id", merchant), ("TenantId", tenant), ("Name", "exact-http-amount"),
            ("ChannelKey", "wechat_native"), ("AppId", "test"), ("MerchantId", "test"), ("CertificateSerialNo", "test"),
            ("NotifyUrl", "https://example.com/notify"), ("ReturnUrl", null), ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"),
            ("IsDefault", false), ("IsEnabled", true), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)));
        var request = new CreatePaymentOrderRequest(tenant, merchant, "wechat_native", long.MaxValue, "CNY", "Exact amount 中文<>", null, Guid.CreateVersion7());
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var invalid = JsonSerializer.SerializeToNode(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        foreach (var value in new[] { "9007199254740993", "\"01\"", "\"1e3\"", "\"9223372036854775808\"", "null" })
        {
            invalid["amountMinor"] = JsonNode.Parse(value);
            using var rejected = await host.PostAsJsonAsync("/api/v1/payments/orders", invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
            Assert.AreEqual(0, channel.CreateCalls);
            Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_order WHERE IdempotencyKey=@Id", new { Id = request.IdempotencyKey }));
        }
        using var created = await host.PostAsJsonAsync("/api/v1/payments/orders", request);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var order = await created.Content.ReadFromJsonAsync<JsonElement>(); var orderId = order.GetProperty("id").GetGuid();
        Assert.AreEqual("9223372036854775807", order.GetProperty("amountMinor").GetString());
        // 冻结旧数值字段布局作为独立参照，线格式演进不能改变已经保存的创建摘要。
        var baseline = new { request.TenantId, request.MerchantConfigId, request.ChannelKey, request.AmountMinor,
            request.Currency, request.Subject, request.Description, request.IdempotencyKey };
        var baselineHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(baseline)));
        Assert.AreEqual(baselineHash, await database.ExecuteScalarAsync<string>("SELECT RequestHash FROM fn_payment_order WHERE Id=@Id", new { Id = orderId }));
        using var replay = await host.PostAsJsonAsync("/api/v1/payments/orders", request);
        Assert.AreEqual(HttpStatusCode.Created, replay.StatusCode, await replay.Content.ReadAsStringAsync());
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(orderId, replayed.GetProperty("id").GetGuid()); Assert.AreEqual(order.GetProperty("version").GetInt32(), replayed.GetProperty("version").GetInt32());
        Assert.AreEqual(1, channel.CreateCalls);
        Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_order WHERE IdempotencyKey=@Id", new { Id = request.IdempotencyKey }));
        Assert.AreEqual(long.MaxValue, (await host.GetFromJsonAsync<PaymentOrderResponse>($"/api/v1/payments/orders/{orderId:D}"))!.AmountMinor);
        var page = await host.GetFromJsonAsync<JsonElement>($"/api/v1/payments/orders?tenantId={tenant:D}&outTradeNoContains={order.GetProperty("outTradeNo").GetString()}");
        Assert.AreEqual("9223372036854775807", page.GetProperty("items").EnumerateArray().Single().GetProperty("amountMinor").GetString());
        await MarkPaid(database, orderId);
        using var invalidRefund = await host.PostAsJsonAsync($"/api/v1/payments/orders/{orderId:D}/refunds", new { amountMinor = 9_007_199_254_740_993L, reason = "partial" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidRefund.StatusCode); Assert.AreEqual(0, channel.RefundAmounts.Count);
        Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_refund WHERE OrderId=@Id", new { Id = orderId }));
        using var refunded = await host.PostAsJsonAsync($"/api/v1/payments/orders/{orderId:D}/refunds", new CreatePaymentRefundRequest(9_007_199_254_740_993L, "partial"));
        Assert.AreEqual(HttpStatusCode.Created, refunded.StatusCode, await refunded.Content.ReadAsStringAsync());
        var refund = await refunded.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("9007199254740993", refund.GetProperty("amountMinor").GetString());
        var refundId = refund.GetProperty("id").GetGuid();
        Assert.AreEqual(9_007_199_254_740_993L, (await host.GetFromJsonAsync<PaymentRefundResponse>($"/api/v1/payments/refunds/{refundId:D}"))!.AmountMinor);
        var refunds = await host.GetFromJsonAsync<JsonElement>($"/api/v1/payments/refunds?orderId={orderId:D}");
        Assert.AreEqual("9007199254740993", refunds.GetProperty("items").EnumerateArray().Single().GetProperty("amountMinor").GetString());
        using var second = await host.PostAsJsonAsync("/api/v1/payments/orders", request with { IdempotencyKey = Guid.CreateVersion7() });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode); var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await MarkPaid(database, secondId);
        using var full = await host.PostAsJsonAsync($"/api/v1/payments/orders/{secondId:D}/refunds", new CreatePaymentRefundRequest(null, "full"));
        Assert.AreEqual(HttpStatusCode.Created, full.StatusCode, await full.Content.ReadAsStringAsync());
        Assert.AreEqual("9223372036854775807", (await full.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amountMinor").GetString());
        CollectionAssert.AreEqual(new[] { 9_007_199_254_740_993L, long.MaxValue }, channel.RefundAmounts.ToArray()); Assert.AreEqual(2, channel.CreateCalls);
    }

    private static Task<int> MarkPaid(System.Data.Common.DbConnection database, Guid id) => database.ExecuteAsync(
        "UPDATE fn_payment_order SET TradeStateKey='succeeded',ProviderTransactionId='paid',PaidAtUtc=@Now,Version=Version+1 WHERE Id=@Id", new { Id = id, Now = DateTime.UtcNow });
    private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value);
    private sealed class ExactAmountChannel : IWeChatNativePayClient
    {
        internal int CreateCalls; internal List<long> RefundAmounts { get; } = [];
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default)
        { Assert.AreEqual(long.MaxValue, amountMinor); CreateCalls++; return Task.FromResult(WeChatNativePayResult.Success("weixin://exact-test")); }
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default)
        { Assert.AreEqual(long.MaxValue, totalMinor); RefundAmounts.Add(amountMinor); return Task.FromResult(WeChatRefundResult.Success(outRefundNo, "PROCESSING")); }
    }
}
