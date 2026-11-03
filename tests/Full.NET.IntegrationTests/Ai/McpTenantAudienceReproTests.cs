using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.NativeAot;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>确认专用资源令牌不能直接用于普通 API 的上下文切换入口。</summary>
[TestClass, DoNotParallelize]
public sealed class McpTenantAudienceReproTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Dedicated_resource_token_is_rejected_by_ordinary_tenant_context_endpoint(DatabaseProvider provider)
    {
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_TENANT_REPRO_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a04-tenant-repro", stamp);
        var keys = Path.Combine(directory, "keys");
        var files = Path.Combine(directory, "files");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database, directory, keys, files }) + "\n");
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
            ["Identity:Oidc:Clients:0:ResourceAudience"] = resource,
            ["FullNet:Ai:Mcp:ResourceIdentifier"] = resource,
            ["Files:Local:RootPath"] = files,
            ["DataProtection:KeyRingPath"] = keys,
        };
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        using var control = factory.CreateClientForHost("localhost");
        var hostToken = await NativeApiE2EAssertions.LoginAsync(control, "A04 tenant resource repro");
        using var available = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        available.Headers.Authorization = new("Bearer", hostToken);
        using var tenantsResponse = await control.SendAsync(available);
        Assert.AreEqual(HttpStatusCode.OK, tenantsResponse.StatusCode);
        using var tenants = JsonDocument.Parse(await tenantsResponse.Content.ReadAsStringAsync());
        var tenantId = tenants.RootElement.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "acme").GetProperty("id").GetGuid();
        using var browser = factory.CreateClientForHost("localhost");
        var dedicated = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: true, resource: resource);
        Assert.IsFalse(string.IsNullOrWhiteSpace(dedicated.AccessToken));
        Assert.IsTrue(new JsonWebToken(dedicated.AccessToken).Audiences.Contains(resource, StringComparer.Ordinal));
        using var change = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context");
        change.Headers.Authorization = new("Bearer", dedicated.AccessToken);
        change.Content = JsonContent.Create(new { tenantId });
        using var changed = await browser.SendAsync(change);
        Assert.AreEqual(HttpStatusCode.Unauthorized, changed.StatusCode, "普通 API 不能接受仅面向 MCP 资源的令牌。");
    }
}
