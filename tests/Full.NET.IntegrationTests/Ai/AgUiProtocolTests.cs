using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>AG-UI SSE 重放持久事件；重连只读游标，不能触发工具重跑。</summary>
[TestClass]
public sealed class AgUiProtocolSqlServerTests
{
    [TestMethod]
    public async Task Stream_replays_standard_events_without_side_effects_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AgUiProtocolAssertions.VerifyAsync(factory);
    }
}

[TestClass]
public sealed class AgUiProtocolMySqlTests
{
    [TestMethod]
    public async Task Stream_replays_standard_events_without_side_effects_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AgUiProtocolAssertions.VerifyAsync(factory);
    }
}

internal static class AgUiProtocolAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] permissions =
        [
            AiModelPermissions.Read,
            AiModelPermissions.Create,
            AiAgentRunPermissions.Read,
            AiAgentRunPermissions.Create,
            AiAgentRunPermissions.Cancel,
        ];
        var owner = await factory.CreateHostIdentityAsync($"ag-ui-owner-{Guid.NewGuid():N}", permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "AG-UI 模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;

        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var accepted = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(
            Guid.CreateVersion7(),
            "fullnet-single-text-v1",
            model.Id,
            "ag-ui stream test",
            100,
            100));
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var created = (await accepted.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;

        using var cancelled = await client.PostAsync($"/api/v1/ai/agent/runs/{created.RunId}/cancel", null);
        Assert.IsTrue(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());

        using var streamResponse = await client.GetAsync(
            $"/api/v1/ai/agent/runs/{created.RunId}/events/stream",
            HttpCompletionOption.ResponseHeadersRead);
        Assert.AreEqual(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.AreEqual("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);

        var body = await streamResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: RUN_STARTED", body, StringComparison.Ordinal);
        Assert.Contains("event: STATE_SNAPSHOT", body, StringComparison.Ordinal);
        Assert.IsTrue(
            body.Contains("event: RUN_FINISHED", StringComparison.Ordinal)
            || body.Contains("event: RUN_ERROR", StringComparison.Ordinal),
            "Terminal AG-UI boundary event must be present.");

        using var expiredCursor = await client.GetAsync(
            $"/api/v1/ai/agent/runs/{created.RunId}/events/stream?afterSequence=9999");
        Assert.AreEqual(HttpStatusCode.Conflict, expiredCursor.StatusCode);
        await AiAgentRunApiAssertions.AssertProblemAsync(expiredCursor, HttpStatusCode.Conflict, AiErrorCodes.AgentRunEventsCursorExpired);
    }
}
