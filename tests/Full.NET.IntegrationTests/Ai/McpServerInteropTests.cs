using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Agents.Mcp;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>标准 MCP 客户端可完成 initialize、工具/资源/提示读取与受控调用。</summary>
[TestClass]
public sealed class McpServerInteropSqlServerTests
{
    [TestMethod]
    public async Task Standard_client_can_list_call_and_read_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await McpServerInteropAssertions.VerifyAsync(factory);
    }
}

[TestClass]
public sealed class McpServerInteropMySqlTests
{
    [TestMethod]
    public async Task Standard_client_can_list_call_and_read_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await McpServerInteropAssertions.VerifyAsync(factory);
    }
}

internal static class McpServerInteropAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] permissions =
        [
            AiModelPermissions.Read,
            AiModelPermissions.Create,
            AiChatPermissions.Read,
            AiChatPermissions.Create,
            AiAgentToolPermissions.CatalogRead,
        ];
        var owner = await factory.CreateHostIdentityAsync($"mcp-owner-{Guid.NewGuid():N}", permissions);
        var other = await factory.CreateHostIdentityAsync($"mcp-other-{Guid.NewGuid():N}", permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "MCP 模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;

        using var sessionResponse = await client.PostAsJsonAsync(
            "/api/v1/ai/chat/sessions",
            new CreateAiChatSessionRequest(model.Id, "MCP 会话"));
        Assert.IsTrue(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
        using var otherSessionResponse = await client.PostAsJsonAsync(
            "/api/v1/ai/chat/sessions",
            new CreateAiChatSessionRequest(model.Id, "其他会话"));
        Assert.IsTrue(otherSessionResponse.IsSuccessStatusCode, await otherSessionResponse.Content.ReadAsStringAsync());

        var httpClient = factory.CreateClientForHost("localhost");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var endpoint = new Uri(httpClient.BaseAddress!, "/api/v1/ai/mcp");
        await using var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = endpoint },
            httpClient));

        var tools = await mcpClient.ListToolsAsync();
        var toolNames = tools.Select(tool => tool.Name).ToArray();
        CollectionAssert.Contains(toolNames, "ai.tools.ping");
        CollectionAssert.Contains(toolNames, "ai.models.list");
        CollectionAssert.Contains(toolNames, "ai.chat.sessions.list");
        CollectionAssert.DoesNotContain(toolNames, "ai.chat.sessions.rename");

        var ping = await mcpClient.CallToolAsync("ai.tools.ping", new Dictionary<string, object?>());
        Assert.IsFalse(ping.IsError ?? false, ping.Content?.FirstOrDefault()?.ToString());

        var ownedUri = McpExposurePolicy.SessionSummaryUriTemplate.Replace(
            "{sessionId}",
            session.Id.ToString("D"),
            StringComparison.Ordinal);
        var resource = await mcpClient.ReadResourceAsync(ownedUri);
        Assert.IsTrue(resource.Contents.Count > 0);
        var ownedText = (resource.Contents[0] as TextResourceContents)?.Text ?? string.Empty;
        using var ownedSummary = JsonDocument.Parse(ownedText);
        Assert.AreEqual(session.Id, ownedSummary.RootElement.GetProperty("sessionId").GetGuid());
        Assert.AreEqual(session.Title, ownedSummary.RootElement.GetProperty("title").GetString());

        var foreignUri = McpExposurePolicy.SessionSummaryUriTemplate.Replace(
            "{sessionId}",
            Guid.CreateVersion7().ToString("D"),
            StringComparison.Ordinal);
        var foreignResource = await mcpClient.ReadResourceAsync(foreignUri);
        var foreignText = (foreignResource.Contents[0] as TextResourceContents)?.Text ?? string.Empty;
        Assert.Contains("not authorized", foreignText, StringComparison.OrdinalIgnoreCase);

        var prompt = await mcpClient.GetPromptAsync(
            McpExposurePolicy.SessionSummaryPromptName,
            new Dictionary<string, object?> { ["sessionId"] = session.Id.ToString("D") });
        Assert.IsTrue(prompt.Messages.Count > 0);
    }
}
