using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Api;

/// <summary>正式双库、Host HTTP 与预留/确认链验证配额摘要，不把账面计量等同于业务许可。</summary>
[TestClass]
public sealed class TenantQuotaUsageApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Usage_reads_exact_official_metrics_with_host_isolation(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var jwt = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"); client.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        var tenants = (await client.GetFromJsonAsync<PagedResult<TenantSummary>>("/api/v1/tenancy/tenants?page=1&pageSize=20"))!;
        var tenant = tenants.Items.Single(item => item.Identifier == "acme");
        var prefix = $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota";
        foreach (var request in new[] {
            new UpsertTenantQuotaMetricRequest(TenantQuotaMetricCodes.FilesStorageBytes, TenantQuotaDefaults.PeriodKey, long.MaxValue),
            new UpsertTenantQuotaMetricRequest(TenantQuotaMetricCodes.IdentitySeats, TenantQuotaDefaults.PeriodKey, 100),
            new UpsertTenantQuotaMetricRequest(TenantQuotaMetricCodes.FilesStorageBytes, "2026-11", 10),
            new UpsertTenantQuotaMetricRequest("custom.not_official", TenantQuotaDefaults.PeriodKey, 10) })
        {
            using var write = await client.PutAsJsonAsync(prefix + "/metrics", request);
            Assert.AreEqual(HttpStatusCode.OK, write.StatusCode, await write.Content.ReadAsStringAsync());
        }
        var consumed = Guid.CreateVersion7().ToString("D");
        using (var reserved = await client.PostAsJsonAsync(prefix + "/reserve", new ReserveTenantQuotaRequest(TenantQuotaMetricCodes.FilesStorageBytes, consumed, long.MaxValue - 100)))
            Assert.AreEqual(HttpStatusCode.OK, reserved.StatusCode, await reserved.Content.ReadAsStringAsync());
        using (var confirmed = await client.PostAsJsonAsync(prefix + "/confirm", new ConfirmTenantQuotaRequest(consumed) { MetricCode = TenantQuotaMetricCodes.FilesStorageBytes }))
            Assert.AreEqual(HttpStatusCode.OK, confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        using (var pending = await client.PostAsJsonAsync(prefix + "/reserve", new ReserveTenantQuotaRequest(TenantQuotaMetricCodes.FilesStorageBytes, Guid.CreateVersion7().ToString("D"), 25)))
            Assert.AreEqual(HttpStatusCode.OK, pending.StatusCode, await pending.Content.ReadAsStringAsync());
        using var response = await client.GetAsync(prefix + "/usage");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement; Assert.AreEqual(tenant.Id, root.GetProperty("tenantId").GetGuid());
        Assert.IsTrue(root.GetProperty("tenantIsActive").GetBoolean()); Assert.IsTrue(root.GetProperty("readAtUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        var metrics = root.GetProperty("metrics").EnumerateArray().ToArray(); Assert.AreEqual(2, metrics.Length);
        Assert.IsTrue(metrics.All(metric => metric.GetProperty("periodKey").GetString() == TenantQuotaDefaults.PeriodKey));
        var storage = metrics.Single(metric => metric.GetProperty("metricCode").GetString() == TenantQuotaMetricCodes.FilesStorageBytes);
        Assert.AreEqual("9223372036854775807", storage.GetProperty("limitValue").GetString());
        Assert.AreEqual("9223372036854775707", storage.GetProperty("usedValue").GetString()); Assert.AreEqual("25", storage.GetProperty("reservedValue").GetString());
        using (var absent = await client.GetAsync($"/api/v1/tenancy/tenants/{Guid.CreateVersion7():D}/quota/usage")) Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        using (var anonymous = factory.CreateClientForHost("localhost"))
        using (var denied = await anonymous.GetAsync(prefix + "/usage")) Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        var tenantJwt = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, jwt, tenant.Id);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tenantJwt);
        using (var forbidden = await client.GetAsync(prefix + "/usage")) Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"));
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        // 只为只读分支准备孤立库状态，不冒充企业暂停或实际文件写入旅程。
        await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=0 WHERE Id=@TenantId", new { TenantId = tenant.Id });
        using (var inactive = await client.GetAsync(prefix + "/usage"))
        {
            Assert.AreEqual(HttpStatusCode.OK, inactive.StatusCode); using var body = JsonDocument.Parse(await inactive.Content.ReadAsStringAsync());
            Assert.IsFalse(body.RootElement.GetProperty("tenantIsActive").GetBoolean());
        }
        await db.ExecuteAsync("DELETE FROM fn_tenancy_quota_reservation WHERE TenantId=@TenantId", new { TenantId = tenant.Id });
        await db.ExecuteAsync("DELETE FROM fn_tenancy_quota_metric WHERE TenantId=@TenantId", new { TenantId = tenant.Id });
        using var empty = await client.GetAsync(prefix + "/usage"); Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()); Assert.AreEqual(0, emptyJson.RootElement.GetProperty("metrics").GetArrayLength());
    }
}
