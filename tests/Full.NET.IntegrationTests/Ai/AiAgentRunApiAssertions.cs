using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证运行就绪门禁、幂等创建、所有权读取与取消。</summary>
internal static class AiAgentRunApiAssertions
{
    private const string DefinitionKey = "fullnet-single-text-v1";

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
            AiPromptPermissions.Read, AiPromptPermissions.Create, AiPromptPermissions.Append,
            AiPromptPermissions.Publish, AiPromptPermissions.Deactivate, AiPromptPermissions.Rollback, AiPromptPermissions.Use,
            "tenancy.tenants.switch",
        ];
        var owner = await factory.CreateHostIdentityAsync($"agent-run-owner-{Guid.NewGuid():N}", permissions);
        var other = await factory.CreateHostIdentityAsync($"agent-run-other-{Guid.NewGuid():N}", permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "Agent 模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        await AiPromptApiAssertions.VerifyAsync(client, model.Id);
        var clientRequestId = Guid.CreateVersion7();
        var createRequest = new CreateAiAgentRunRequest(
            clientRequestId,
            DefinitionKey,
            model.Id,
            "integration prompt",
            100,
            100);

        using var rejected = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", createRequest);
        await AssertProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentRuntimeUnavailable);

        await SeedFreshWorkerHeartbeatAsync(factory);
        await AiPromptApiAssertions.VerifyRunAsync(client, model.Id, factory);
        using var accepted = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", createRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var created = await accepted.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>();
        Assert.IsNotNull(created);
        Assert.AreNotEqual(Guid.Empty, created.RunId);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", createRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>();
        Assert.AreEqual(created.RunId, duplicateBody!.RunId);

        using var read = await client.GetAsync($"/api/v1/ai/agent/runs/{created.RunId}");
        Assert.IsTrue(read.IsSuccessStatusCode, await read.Content.ReadAsStringAsync());
        var run = await read.Content.ReadFromJsonAsync<AiAgentRunResponse>();
        Assert.AreEqual(created.RunId, run!.Id);
        Assert.AreEqual("queued", run.StatusKey);
        Assert.AreEqual(DefinitionKey, run.DefinitionKey);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
        using var foreignRead = await client.GetAsync($"/api/v1/ai/agent/runs/{created.RunId}");
        await AssertProblemAsync(foreignRead, HttpStatusCode.NotFound, AiErrorCodes.AgentRunNotFound);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var cancelled = await client.PostAsync($"/api/v1/ai/agent/runs/{created.RunId}/cancel", null);
        Assert.IsTrue(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());
        Assert.IsTrue(await cancelled.Content.ReadFromJsonAsync<bool>());

        using var afterCancel = await client.GetAsync($"/api/v1/ai/agent/runs/{created.RunId}");
        var cancelledRun = await afterCancel.Content.ReadFromJsonAsync<AiAgentRunResponse>();
        Assert.AreEqual("cancelled", cancelledRun!.StatusKey);

        using var cancelAgain = await client.PostAsync($"/api/v1/ai/agent/runs/{created.RunId}/cancel", null);
        await AssertProblemAsync(cancelAgain, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentRunNotCancellable);
        await AiPromptApiAssertions.VerifyTenantAsync(client, model.Id, factory, owner.AccessToken);
        await AiAgentCancellationAssertions.VerifyAsync(factory);
        // 既有 Prompt 场景检查精确预留数量；新增路由场景在末尾创建自己的运行，避免共享夹具相互污染。
        await SeedFreshWorkerHeartbeatAsync(factory);
        await VerifyControlledToolRouteAsync(client, model.Id, factory);
    }

    internal static async Task SeedFreshWorkerHeartbeatAsync(FullNetApiFactory factory)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync("""
            INSERT INTO fn_ai_agent_worker_instance
                (InstanceId, WorkerRole, RuntimeVersion, HostProfile, StartedAtUtc, LastHeartbeatAtUtc)
            VALUES (@InstanceId, @WorkerRole, @RuntimeVersion, @HostProfile, @StartedAtUtc, @LastHeartbeatAtUtc)
            """, new
        {
            InstanceId = Guid.CreateVersion7(),
            WorkerRole = "ai-agent",
            RuntimeVersion = "1",
            HostProfile = "integration",
            StartedAtUtc = now,
            LastHeartbeatAtUtc = now,
        });
    }

    private static async Task VerifyControlledToolRouteAsync(HttpClient client, Guid modelId, FullNetApiFactory factory)
    {
        const string path = "/api/v1/ai/tasks/authorized-tool-task";
        var task = new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-chat-rename-workflow-v1", modelId, "", 100, 100);
        var request = new RouteAiTaskRequest("authorized_tool_task", ToolTask: task);
        foreach (var invalid in new[] { request with { IntentKey = "unknown" }, request with { SessionId = Guid.CreateVersion7() },
            request with { ToolTask = task with { DefinitionKey = DefinitionKey } },
            request with { ToolTask = task with { DefinitionKey = "fullnet-readonly-tool-loop-v1" } } })
        {
            using var rejected = await client.PostAsJsonAsync(path, invalid);
            await AssertProblemAsync(rejected, HttpStatusCode.BadRequest, "ai.routing.invalid");
        }
        var denied = await factory.CreateHostIdentityAsync($"route-denied-{Guid.NewGuid():N}", [AiAgentRunPermissions.Read]);
        using var other = factory.CreateClientForHost("localhost");
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", denied.AccessToken);
        using (var forbidden = await other.PostAsJsonAsync(path, request)) Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var accepted = await client.PostAsJsonAsync(path, request);
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
        var run = await accepted.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>(); Assert.IsNotNull(run);
        Assert.AreEqual($"/api/v1/ai/agent/runs/{run.RunId}", accepted.Headers.Location!.OriginalString);
        using var repeated = await client.PostAsJsonAsync(path, request);
        Assert.AreEqual(HttpStatusCode.Accepted, repeated.StatusCode, await repeated.Content.ReadAsStringAsync());
        Assert.AreEqual(run.RunId, (await repeated.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId);
        using var read = await client.GetAsync($"/api/v1/ai/agent/runs/{run.RunId}");
        var saved = await read.Content.ReadFromJsonAsync<AiAgentRunResponse>(); Assert.IsNotNull(saved);
        Assert.AreEqual(task.DefinitionKey, saved.DefinitionKey); Assert.AreEqual(2, saved.DefinitionVersion);
        using (var conflict = await client.PostAsJsonAsync(path, request with { ToolTask = task with { OutputTokenLimit = 101 } }))
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "ai.routing.conflict");
        var legacyRequest = task with { ClientRequestId = Guid.CreateVersion7(), DefinitionKey = DefinitionKey, Prompt = "legacy" };
        using var legacy = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", legacyRequest);
        Assert.AreEqual(HttpStatusCode.Accepted, legacy.StatusCode, await legacy.Content.ReadAsStringAsync());
        using var crossDefinition = await client.PostAsJsonAsync(path, request with { ToolTask = task with { ClientRequestId = legacyRequest.ClientRequestId } });
        await AssertProblemAsync(crossDefinition, HttpStatusCode.Conflict, "ai.routing.conflict");
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
    }
}
