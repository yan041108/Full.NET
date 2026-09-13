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
        ];
        var owner = await factory.CreateHostIdentityAsync($"agent-run-owner-{Guid.NewGuid():N}", permissions);
        var other = await factory.CreateHostIdentityAsync($"agent-run-other-{Guid.NewGuid():N}", permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "Agent 模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
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

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
    }
}
