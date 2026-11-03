using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>旧互操作场景使用真实 OIDC 专用资源令牌和准确登记的独立数据库。</summary>
internal static class McpOidcInteropFixture
{
    internal static async Task<FullNetApiFactory> CreateAsync(DatabaseProvider provider, Action<IServiceCollection>? configure = null)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:Clients:0:ResourceAudience"] = resource,
            ["FullNet:Ai:Mcp:ResourceIdentifier"] = resource,
            ["FullNet:Ai:Network:PrivateOnly"] = "true",
            ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "http://localhost",
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-interop-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-interop-keys", stamp),
        };
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_INTEROP_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        return new FullNetApiFactory(provider, connection, settings, configureTestServices: configure);
    }

    internal static async Task<string> IssueAsync(FullNetApiFactory factory, string username)
    {
        using var browser = factory.CreateClientForHost("localhost");
        var issued = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, username, FullNetApiFactory.TestPassword, requestOfflineAccess: false,
            resource: "https://mcp.example.test/api/v1/ai/mcp");
        Assert.IsFalse(string.IsNullOrWhiteSpace(issued.AccessToken), "OIDC 必须真实签发专用资源令牌。");
        return issued.AccessToken;
    }
}
