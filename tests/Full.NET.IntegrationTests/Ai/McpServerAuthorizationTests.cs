using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MCP 端点必须要求 Bearer 授权；受保护资源元数据可匿名发现。</summary>
[TestClass]
public sealed class McpServerAuthorizationSqlServerTests
{
    [TestMethod]
    public async Task Mcp_requires_bearer_and_publishes_metadata_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await McpServerAuthorizationAssertions.VerifyAsync(factory);
    }
}

/// <summary>集成测试：McpServerAuthorizationMySql。</summary>
[TestClass]
public sealed class McpServerAuthorizationMySqlTests
{
    [TestMethod]
    public async Task Mcp_requires_bearer_and_publishes_metadata_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await McpServerAuthorizationAssertions.VerifyAsync(factory);
    }
}

internal static class McpServerAuthorizationAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");

        using var metadata = await client.GetAsync("/.well-known/oauth-protected-resource/ai/mcp");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, metadata.StatusCode,
            "未启用正式 OIDC 或未配置 HTTPS 资源时不能发布虚假的授权服务器。");

        using var unauthorized = await client.PostAsync(
            "/api/v1/ai/mcp",
            new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
                Encoding.UTF8,
                "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
}
