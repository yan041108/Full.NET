using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>原创建身份的正式 HTTP 读取只需独立读取权限，绑定 Host、租户与键且不修改资金意图。</summary>
[TestClass]
public sealed class PaymentOrderCreationLookupApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Host_creation_lookup_is_readonly_exact_and_permission_separated(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var readAt = DateTimeOffset.UtcNow;
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IClock>(new FixedClock(readAt))));
        await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme").Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var id = Guid.CreateVersion7(); var key = Guid.CreateVersion7();
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(PaymentOrderSql.Insert,
            new Dictionary<string, object?> { ["Id"] = id, ["TenantId"] = tenant, ["MerchantConfigId"] = Guid.CreateVersion7(),
                ["ChannelKey"] = "wechat_native", ["OutTradeNo"] = id.ToString("N"), ["TradeStateKey"] = "provider_unknown",
                ["AmountMinor"] = long.MaxValue, ["Currency"] = "CNY", ["Subject"] = "Original", ["Description"] = null,
                ["CodeUrl"] = null, ["ProviderTransactionId"] = null, ["FailMessage"] = null, ["CreatedAtUtc"] = readAt.AddDays(-1),
                ["UpdatedAtUtc"] = readAt, ["PaidAtUtc"] = null, ["Version"] = 7, ["IdempotencyKey"] = key, ["RequestHash"] = new string('A', 64) });
        var path = $"/api/v1/payments/orders/creation-requests/{key:D}?tenantId={tenant:D}";
        using var response = await host.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(key, body.GetProperty("idempotencyKey").GetGuid()); Assert.AreEqual(readAt, body.GetProperty("readAtUtc").GetDateTimeOffset());
        Assert.AreEqual(id, body.GetProperty("order").GetProperty("id").GetGuid());
        Assert.AreEqual("9223372036854775807", body.GetProperty("order").GetProperty("amountMinor").GetString());
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("requestHash", StringComparison.OrdinalIgnoreCase));
        using var foreign = await host.GetAsync($"/api/v1/payments/orders/creation-requests/{key:D}?tenantId={Guid.CreateVersion7():D}");
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        foreach (var locale in new[] { "zh-CN", "en-US" })
        {
            host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd(locale);
            using var missing = await host.GetAsync($"/api/v1/payments/orders/creation-requests/{Guid.CreateVersion7():D}?tenantId={tenant:D}");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
            var problem = await missing.Content.ReadFromJsonAsync<JsonElement>(); Assert.AreEqual(PaymentErrorCodes.OrderCreationRequestNotFound, problem.GetProperty("code").GetString());
            Assert.AreEqual(locale == "zh-CN" ? "未找到该租户及原创建请求对应的订单。" : "No order was found for this tenant and original creation request.", problem.GetProperty("title").GetString());
        }
        using var emptyKey = await host.GetAsync($"/api/v1/payments/orders/creation-requests/{Guid.Empty:D}?tenantId={tenant:D}");
        using var emptyTenant = await host.GetAsync($"/api/v1/payments/orders/creation-requests/{key:D}?tenantId={Guid.Empty:D}");
        Assert.AreEqual(HttpStatusCode.NotFound, emptyKey.StatusCode); Assert.AreEqual(HttpStatusCode.NotFound, emptyTenant.StatusCode);
        using var anonymous = factory.CreateClientForHost("localhost"); using var unauthenticated = await anonymous.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var readerName = "creation-reader-" + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(readerName, [PaymentOrderPermissions.Read], password: FullNetApiFactory.TestPassword);
        using var reader = factory.CreateClientForHost("localhost"); reader.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(reader, readerName));
        using var readOnly = await reader.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, readOnly.StatusCode);
        var writerName = "creation-writer-" + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(writerName, [PaymentOrderPermissions.Create], password: FullNetApiFactory.TestPassword);
        using var writer = factory.CreateClientForHost("localhost"); writer.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(writer, writerName));
        using var denied = await writer.GetAsync(path); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant));
        using var tenantDenied = await host.GetAsync(path); Assert.AreEqual(HttpStatusCode.Forbidden, tenantDenied.StatusCode);
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        Assert.AreEqual(7, await database.ExecuteScalarAsync<int>("SELECT Version FROM fn_payment_order WHERE Id=@Id", new { Id = id }));
        Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_order WHERE TenantId=@Tenant AND IdempotencyKey=@Key", new { Tenant = tenant, Key = key }));
    }
    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow => value; }
}
