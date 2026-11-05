using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>正式双库 HTTP 报价发布和账单回执保持完整 Int64 字符串，数字正文失败关闭。</summary>
[TestClass]
public sealed class SubscriptionOfferWirePrecisionApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Offer_and_paid_invoice_preserve_int64_strings_and_reject_numeric_input(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var createdPackage = await scope.ServiceProvider.GetRequiredService<HostTenantPackageManagementService>()
            .CreateAsync(new("wire-" + Guid.CreateVersion7().ToString("N"), "精确金额套餐", null));
        Assert.IsTrue(createdPackage.IsSuccess, createdPackage.Error?.Code); var packageId = createdPackage.Value!.Id;
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        await database.ExecuteAsync("UPDATE fn_tenancy_settings SET EntitlementEnforcementPhase='Enforced'");
        await database.ExecuteAsync("UPDATE fn_tenancy_tenant_subscription SET Status='Expired' WHERE TenantId=@TenantId", new { TenantId = tenant.Id });
        var request = new UpsertTenantPackageOfferRequest(tenant.Id, Guid.CreateVersion7(), long.MaxValue, "CNY",
            9_007_199_254_740_993L, long.MaxValue - 1, 30, [], true, 0);
        var path = $"/api/v1/tenancy/tenant-packages/{packageId:D}/offer";
        using var saved = await host.PutAsJsonAsync(path, request);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        var quote = await saved.Content.ReadFromJsonAsync<JsonElement>(); AssertQuote(quote);
        AssertQuote(await host.GetFromJsonAsync<JsonElement>(path));
        var numeric = JsonNode.Parse(JsonSerializer.Serialize(request with { Version = 1 }, TenancyJsonSerializerContext.Default.UpsertTenantPackageOfferRequest))!;
        numeric["amountMinor"] = JsonValue.Create(9_007_199_254_740_993L);
        using var invalid = await host.PutAsync(path, new StringContent(numeric.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(1, (await host.GetFromJsonAsync<JsonElement>(path)).GetProperty("version").GetInt32());
        var create = new CreatePaidTenantSubscriptionRequest(packageId, 1, DateTimeOffset.UtcNow.AddMinutes(-1), Guid.CreateVersion7());
        var paidPath = $"/api/v1/tenancy/tenants/{tenant.Id:D}/subscriptions/paid";
        using var first = await host.PostAsJsonAsync(paidPath, create);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        var invoice = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("9223372036854775807", invoice.GetProperty("amountMinor").GetString());
        Assert.AreEqual(packageId, invoice.GetProperty("packageId").GetGuid());
        var subscriptionId = invoice.GetProperty("subscriptionId").GetGuid();
        using var replay = await host.PostAsJsonAsync(paidPath, create);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(subscriptionId, replayed.GetProperty("subscriptionId").GetGuid());
        Assert.AreEqual("9223372036854775807", replayed.GetProperty("amountMinor").GetString());
        var frozen = await host.GetFromJsonAsync<JsonElement>($"/api/v1/tenancy/tenants/{tenant.Id:D}/subscriptions/{subscriptionId:D}/invoice");
        Assert.AreEqual("9223372036854775807", frozen.GetProperty("amountMinor").GetString());
        Assert.AreEqual("9007199254740993", frozen.GetProperty("seatLimit").GetString());
        Assert.AreEqual("9223372036854775806", frozen.GetProperty("storageLimit").GetString());
    }

    private static void AssertQuote(JsonElement quote)
    {
        Assert.AreEqual("9223372036854775807", quote.GetProperty("amountMinor").GetString());
        Assert.AreEqual("9007199254740993", quote.GetProperty("seatLimit").GetString());
        Assert.AreEqual("9223372036854775806", quote.GetProperty("storageLimit").GetString());
    }
}
