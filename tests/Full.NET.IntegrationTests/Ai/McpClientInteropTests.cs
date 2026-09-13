using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.Agents.Mcp;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>已批准远端 MCP 工具经统一执行器调用；不透传用户访问令牌。</summary>
[TestClass]
public sealed class McpClientInteropSqlServerTests
{
    [TestMethod]
    public async Task Approved_remote_ping_executes_via_unified_executor_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?>
            {
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "http://localhost",
            },
            configureTestServices: McpLoopbackHttpClientTestSupport.ConfigureServices);
        await McpClientInteropAssertions.VerifyAsync(factory);
    }
}

[TestClass]
public sealed class McpClientInteropMySqlTests
{
    [TestMethod]
    public async Task Approved_remote_ping_executes_via_unified_executor_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?>
            {
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "http://localhost",
            },
            configureTestServices: McpLoopbackHttpClientTestSupport.ConfigureServices);
        await McpClientInteropAssertions.VerifyAsync(factory);
    }
}

internal static class McpClientInteropAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        McpLoopbackHttpClientTestSupport.Bind(factory);
        var serviceIdentity = await factory.CreateHostIdentityAsync(
            $"mcp-service-{Guid.NewGuid():N}",
            [AiAgentToolPermissions.CatalogRead, AiMcpPermissions.RemoteInvoke]);
        var endpoint = new Uri($"{factory.Server.BaseAddress}api/v1/ai/mcp");
        await SeedRemotePingAsync(factory, serviceIdentity.AccessToken, endpoint).ConfigureAwait(false);

        var localName = McpRemoteCapabilityPolicy.BuildLocalToolName("loopback", "ai.tools.ping");
        using var empty = JsonDocument.Parse("{}");
        var result = await AiToolExecutionAssertions.ExecuteAsync(
            factory,
            serviceIdentity.AccessToken,
            new ToolInvocation(Guid.CreateVersion7(), null, localName, 1, empty.RootElement));
        Assert.AreEqual("succeeded", result.StatusKey);
        Assert.IsTrue(result.Value!.Value.GetProperty("ok").GetBoolean());
    }

    private static async Task SeedRemotePingAsync(FullNetApiFactory factory, string serviceToken, Uri endpoint)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var protector = scope.ServiceProvider.GetRequiredService<AiMcpRemoteTokenProtector>();
        var now = DateTimeOffset.UtcNow;
        var connectionId = Guid.CreateVersion7();
        var approvalId = Guid.CreateVersion7();
        const string schema = """{"type":"object","additionalProperties":false,"properties":{}}""";
        var hash = McpRemoteCapabilityPolicy.ComputeSchemaHash(schema);
        var localName = McpRemoteCapabilityPolicy.BuildLocalToolName("loopback", "ai.tools.ping");
        var insertConnection = factory.Provider == DatabaseProvider.SqlServer
            ? """
              INSERT INTO dbo.fn_ai_mcp_remote_connection
              (Id, ScopeKey, TenantId, ConnectionKey, DisplayName, EndpointUrl, ServiceTokenProtected, IsEnabled, Version, CreatedAtUtc, UpdatedAtUtc)
              VALUES (@Id, 'host', NULL, 'loopback', N'Loopback MCP', @EndpointUrl, @ServiceTokenProtected, 1, 1, @Now, @Now)
              """
            : """
              INSERT INTO fn_ai_mcp_remote_connection
              (Id, ScopeKey, TenantId, ConnectionKey, DisplayName, EndpointUrl, ServiceTokenProtected, IsEnabled, Version, CreatedAtUtc, UpdatedAtUtc)
              VALUES (@Id, 'host', NULL, 'loopback', 'Loopback MCP', @EndpointUrl, @ServiceTokenProtected, 1, 1, @Now, @Now)
              """;
        var insertApproval = factory.Provider == DatabaseProvider.SqlServer
            ? """
              INSERT INTO dbo.fn_ai_mcp_remote_tool_approval
              (Id, ConnectionId, LocalToolName, RemoteToolName, ToolVersion, InputSchemaJson, InputSchemaHash, SideEffectKey, PermissionCode, ApprovalStatusKey, Version, CreatedAtUtc, UpdatedAtUtc)
              VALUES (@ApprovalId, @ConnectionId, @LocalToolName, 'ai.tools.ping', 1, @Schema, @Hash, 'none', @PermissionCode, 'approved', 1, @Now, @Now)
              """
            : """
              INSERT INTO fn_ai_mcp_remote_tool_approval
              (Id, ConnectionId, LocalToolName, RemoteToolName, ToolVersion, InputSchemaJson, InputSchemaHash, SideEffectKey, PermissionCode, ApprovalStatusKey, Version, CreatedAtUtc, UpdatedAtUtc)
              VALUES (@ApprovalId, @ConnectionId, @LocalToolName, 'ai.tools.ping', 1, @Schema, @Hash, 'none', @PermissionCode, 'approved', 1, @Now, @Now)
              """;
        await commands.ExecuteAsync(
            new SqlStatement("ai.seed_mcp_remote_connection", insertConnection, SqlDataScope.Global),
            new Dictionary<string, object?>
            {
                ["Id"] = connectionId,
                ["EndpointUrl"] = endpoint.ToString(),
                ["ServiceTokenProtected"] = protector.Protect(serviceToken),
                ["Now"] = now,
            }).ConfigureAwait(false);
        await commands.ExecuteAsync(
            new SqlStatement("ai.seed_mcp_remote_tool", insertApproval, SqlDataScope.Global),
            new Dictionary<string, object?>
            {
                ["ApprovalId"] = approvalId,
                ["ConnectionId"] = connectionId,
                ["LocalToolName"] = localName,
                ["Schema"] = schema,
                ["Hash"] = hash,
                ["PermissionCode"] = AiMcpPermissions.RemoteInvoke,
                ["Now"] = now,
            }).ConfigureAwait(false);
    }
}
