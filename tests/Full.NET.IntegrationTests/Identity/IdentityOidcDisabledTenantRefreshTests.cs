using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>租户停用后，OIDC 刷新不能仅凭旧应用会话继续签发。</summary>
[TestClass, DoNotParallelize]
public sealed class IdentityOidcDisabledTenantRefreshTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Refresh_rechecks_tenant_active_state(DatabaseProvider provider)
    {
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_TENANT_REFRESH_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a04-tenant-refresh", Guid.CreateVersion7().ToString("N"));
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database, directory }) + "\n");
        Directory.CreateDirectory(directory);
        string connection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var master = await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync();
            await using var admin = new SqlConnection(master);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@Name", new { Name = database }));
            await admin.ExecuteAsync($"CREATE DATABASE [{database}]");
            connection = new SqlConnectionStringBuilder(master) { InitialCatalog = database, Pooling = false }.ConnectionString;
        }
        else
        {
            var root = await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync();
            await using var admin = new MySqlConnection(new MySqlConnectionStringBuilder(root) { Database = "mysql", Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@Name", new { Name = database }));
            await admin.ExecuteAsync($"CREATE DATABASE `{database}`");
            await admin.ExecuteAsync($"GRANT ALL PRIVILEGES ON `{database}`.* TO 'fullnet'@'%'");
            connection = new MySqlConnectionStringBuilder(root) { UserID = "fullnet", Database = database, Pooling = false }.ConnectionString;
        }
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Files:Local:RootPath"] = Path.Combine(directory, "files"),
            ["DataProtection:KeyRingPath"] = Path.Combine(directory, "keys"),
        };
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(client,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: true);
        using var available = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        available.Headers.Authorization = new("Bearer", flow.AccessToken);
        using var tenantResponse = await client.SendAsync(available);
        Assert.AreEqual(HttpStatusCode.OK, tenantResponse.StatusCode);
        using var tenants = JsonDocument.Parse(await tenantResponse.Content.ReadAsStringAsync());
        var tenantId = tenants.RootElement.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "acme").GetProperty("id").GetGuid();
        using var change = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context");
        change.Headers.Authorization = new("Bearer", flow.AccessToken);
        change.Content = JsonContent.Create(new { tenantId });
        using var changed = await client.SendAsync(change);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        var switched = await changed.Content.ReadFromJsonAsync<TenantContextTokenResponse>();
        Assert.IsNotNull(switched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(switched.RefreshToken));
        var active = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(client,
            switched.RefreshToken!, IdentityOidcRelyingPartyFixture.PublicClientId, null, CancellationToken.None);
        Assert.IsTrue(active.IsSuccessStatusCode, "活动租户的刷新应成功。");
        using var refreshed = JsonDocument.Parse(active.RawBody);
        var refreshToken = refreshed.RootElement.GetProperty("refresh_token").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(refreshToken));
        await using var db = provider == DatabaseProvider.SqlServer
            ? (System.Data.Common.DbConnection)new SqlConnection(connection) : new MySqlConnection(connection);
        await db.OpenAsync();
        // 使用唯一标识符定位测试种子，避免测试直连驱动的 Guid 字节序与应用边界不同。
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=0 WHERE Identifier=@Identifier", new { Identifier = "acme" }));
        var disabled = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(client,
            refreshToken!, IdentityOidcRelyingPartyFixture.PublicClientId, null, CancellationToken.None);
        Assert.IsFalse(disabled.IsSuccessStatusCode, "停用租户后不得继续刷新。");
        Assert.IsTrue(disabled.RawBody.Contains("invalid_grant", StringComparison.Ordinal));
    }
}
