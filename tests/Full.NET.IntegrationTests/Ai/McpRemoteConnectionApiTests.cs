using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>管理 API 创建、发现、批准远端 MCP 工具，并经由统一执行器调用。</summary>
[TestClass]
public sealed class McpRemoteConnectionApiSqlServerTests
{
    [TestMethod]
    public async Task Management_api_seeds_executable_remote_tool_async()
    {
        using var factory = await McpRemoteConnectionApiAssertions.CreateFactoryAsync(DatabaseProvider.SqlServer);
        await McpRemoteConnectionApiAssertions.VerifyAsync(factory);
    }
}

[TestClass]
public sealed class McpRemoteConnectionApiMySqlTests
{
    [TestMethod]
    public async Task Management_api_seeds_executable_remote_tool_async()
    {
        using var factory = await McpRemoteConnectionApiAssertions.CreateFactoryAsync(DatabaseProvider.MySql);
        await McpRemoteConnectionApiAssertions.VerifyAsync(factory);
    }
}

internal static class McpRemoteConnectionApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        McpLoopbackHttpClientTestSupport.Bind(factory);
        var serviceIdentity = await factory.CreateHostIdentityAsync(
            $"mcp-service-{Guid.NewGuid():N}",
            [AiAgentToolPermissions.CatalogRead, AiMcpPermissions.RemoteInvoke]);
        string[] managePermissions =
        [
            AiMcpPermissions.Manage,
            AiMcpPermissions.RemoteInvoke,
            AiAgentToolPermissions.CatalogRead,
        ];
        var admin = await factory.CreateHostIdentityAsync($"mcp-admin-{Guid.NewGuid():N}", managePermissions);
        var endpoint = new Uri($"{factory.Server.BaseAddress}api/v1/ai/mcp");
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/ai/mcp/remote-connections",
            new CreateAiMcpRemoteConnectionRequest(
                "loopback",
                "Loopback MCP",
                endpoint.ToString(),
                serviceIdentity.AccessToken,
                null));
        Assert.IsTrue(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        var connection = await createResponse.Content.ReadFromJsonAsync<AiMcpRemoteConnectionResponse>();
        Assert.IsNotNull(connection);

        using var discoverResponse = await client.PostAsync(
            $"/api/v1/ai/mcp/remote-connections/{connection.Id}/discover-tools",
            null);
        Assert.IsTrue(discoverResponse.IsSuccessStatusCode, await discoverResponse.Content.ReadAsStringAsync());
        var discovered = await discoverResponse.Content.ReadFromJsonAsync<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>();
        Assert.IsNotNull(discovered);
        Assert.IsTrue(discovered.Any(item => item.RemoteToolName == "ai.tools.ping"));

        using var approveResponse = await client.PostAsJsonAsync(
            $"/api/v1/ai/mcp/remote-connections/{connection.Id}/approve-tool",
            new ApproveAiMcpRemoteToolRequest("ai.tools.ping", "none", AiMcpPermissions.RemoteInvoke));
        Assert.IsTrue(approveResponse.IsSuccessStatusCode, await approveResponse.Content.ReadAsStringAsync());

        var localName = McpRemoteCapabilityPolicy.BuildLocalToolName("loopback", "ai.tools.ping");
        using var empty = JsonDocument.Parse("{}");
        var result = await AiToolExecutionAssertions.ExecuteAsync(
            factory,
            serviceIdentity.AccessToken,
            new ToolInvocation(Guid.CreateVersion7(), null, localName, 1, empty.RootElement));
        Assert.AreEqual("succeeded", result.StatusKey);
        Assert.IsTrue(result.Value!.Value.GetProperty("ok").GetBoolean());
    }

    internal static async Task<FullNetApiFactory> CreateFactoryAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        return new FullNetApiFactory(
            provider,
            connectionString,
            settingsOverrides: new Dictionary<string, string?>
            {
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "http://localhost",
            },
            configureTestServices: McpLoopbackHttpClientTestSupport.ConfigureServices);
    }
}
