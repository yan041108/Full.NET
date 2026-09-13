using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>审批 API 幂等创建、可读摘要、决定与批准后写工具执行。</summary>
internal static class AiAgentApprovalApiAssertions
{
    private const string DefinitionKey = "fullnet-single-text-v1";

    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] requesterPermissions =
        [
            AiModelPermissions.Read,
            AiModelPermissions.Create,
            AiChatPermissions.Read,
            AiChatPermissions.Create,
            AiChatPermissions.Update,
            AiAgentRunPermissions.Read,
            AiAgentRunPermissions.Create,
            AiAgentRunPermissions.Resume,
            AiAgentApprovalPermissions.Read,
            AiAgentApprovalPermissions.Request,
            AiAgentToolPermissions.CatalogRead,
        ];
        string[] approverPermissions = [AiAgentApprovalPermissions.Read, AiAgentApprovalPermissions.Decide];
        var requester = await factory.CreateHostIdentityAsync($"approval-requester-{Guid.NewGuid():N}", requesterPermissions);
        var approver = await factory.CreateHostIdentityAsync($"approval-approver-{Guid.NewGuid():N}", approverPermissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.AccessToken);

        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "审批模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;

        using var sessionResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "原标题"));
        Assert.IsTrue(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;

        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory).ConfigureAwait(false);
        using var runResponse = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(
            Guid.CreateVersion7(), DefinitionKey, model.Id, "rename after approval", 100, 100));
        Assert.AreEqual(HttpStatusCode.Accepted, runResponse.StatusCode);
        var run = (await runResponse.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;

        var operationId = Guid.CreateVersion7();
        var argumentsJson = JsonSerializer.Serialize(new { sessionId = session.Id, title = "审批后标题" });
        var createRequest = new CreateAiAgentApprovalRequest(
            run.RunId,
            operationId,
            "ai.chat.sessions.rename",
            1,
            argumentsJson);

        using var args = JsonDocument.Parse(argumentsJson);
        var invocation = new ToolInvocation(operationId, run.RunId, "ai.chat.sessions.rename", 1, args.RootElement.Clone());
        var beforeApproval = await ExecuteToolAsync(factory, requester.AccessToken, invocation).ConfigureAwait(false);
        Assert.AreEqual("denied", beforeApproval.StatusKey);
        Assert.AreEqual("ai.tool.approval_required", beforeApproval.ErrorCode);

        using var created = await client.PostAsJsonAsync("/api/v1/ai/agent/approvals", createRequest);
        Assert.IsTrue(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var createdBody = await created.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>();
        Assert.IsNotNull(createdBody);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/ai/agent/approvals", createRequest);
        Assert.IsTrue(duplicate.IsSuccessStatusCode, await duplicate.Content.ReadAsStringAsync());
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>();
        Assert.AreEqual(createdBody!.ApprovalId, duplicateBody!.ApprovalId);

        using var read = await client.GetAsync($"/api/v1/ai/agent/approvals/{createdBody.ApprovalId}");
        Assert.IsTrue(read.IsSuccessStatusCode, await read.Content.ReadAsStringAsync());
        var pending = await read.Content.ReadFromJsonAsync<AiAgentApprovalResponse>();
        Assert.AreEqual("pending", pending!.DecisionKey);
        Assert.AreEqual("原标题", pending.TargetSummary);
        Assert.AreEqual("审批后标题", pending.ChangeSummary);
        Assert.IsFalse(string.IsNullOrWhiteSpace(pending.ActionSummary));

        await SetRunStatusAsync(factory, run.RunId, "awaiting_approval").ConfigureAwait(false);
        using var resumeWhilePending = await client.PostAsync($"/api/v1/ai/agent/runs/{run.RunId}/resume", null);
        await AssertProblemAsync(resumeWhilePending, HttpStatusCode.UnprocessableEntity, AiErrorCodes.AgentRunNotResumable);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approver.AccessToken);
        using var decide = await client.PostAsJsonAsync(
            $"/api/v1/ai/agent/approvals/{createdBody.ApprovalId}/decide",
            new DecideAiAgentApprovalRequest(true, pending.Version));
        Assert.IsTrue(decide.IsSuccessStatusCode, await decide.Content.ReadAsStringAsync());
        var approved = await decide.Content.ReadFromJsonAsync<AiAgentApprovalResponse>();
        Assert.AreEqual("approved", approved!.DecisionKey);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.AccessToken);
        using var resumed = await client.PostAsync($"/api/v1/ai/agent/runs/{run.RunId}/resume", null);
        Assert.IsTrue(resumed.IsSuccessStatusCode, await resumed.Content.ReadAsStringAsync());
        Assert.IsTrue(await resumed.Content.ReadFromJsonAsync<bool>());

        using var runAfterResume = await client.GetAsync($"/api/v1/ai/agent/runs/{run.RunId}");
        var resumedRun = await runAfterResume.Content.ReadFromJsonAsync<AiAgentRunResponse>();
        Assert.AreEqual("queued", resumedRun!.StatusKey);

        var executed = await ExecuteToolAsync(factory, requester.AccessToken, invocation).ConfigureAwait(false);
        Assert.AreEqual("succeeded", executed.StatusKey, executed.ErrorCode ?? "(no error code)");

        await AssertToolAuditBindingAsync(factory, operationId, run.RunId, createdBody!.ApprovalId, argumentsJson).ConfigureAwait(false);

        using var sessionRead = await client.GetAsync($"/api/v1/ai/chat/sessions/{session.Id}");
        Assert.IsTrue(sessionRead.IsSuccessStatusCode, await sessionRead.Content.ReadAsStringAsync());
        var renamed = await sessionRead.Content.ReadFromJsonAsync<AiChatSessionResponse>();
        Assert.AreEqual("审批后标题", renamed!.Title);

        var replay = await ExecuteToolAsync(factory, requester.AccessToken, invocation).ConfigureAwait(false);
        Assert.AreEqual("denied", replay.StatusKey);
        Assert.AreEqual("ai.tool.approval_denied", replay.ErrorCode);
    }

    private static async Task<ToolExecutionResult> ExecuteToolAsync(
        FullNetApiFactory factory,
        string accessToken,
        ToolInvocation invocation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = http.HttpContext;
        http.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebToken(accessToken).Claims, "server-issued-fixture")),
        };
        tenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>()
                .ExecuteAsync(invocation).ConfigureAwait(false);
        }
        finally
        {
            tenant.Clear();
            http.HttpContext = previous;
        }
    }

    private static async Task SetRunStatusAsync(FullNetApiFactory factory, Guid runId, string statusKey)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await connection.ExecuteAsync(
            "UPDATE fn_ai_agent_run SET StatusKey = @StatusKey WHERE Id = @RunId",
            new { RunId = runId, StatusKey = statusKey }).ConfigureAwait(false);
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task AssertToolAuditBindingAsync(
        FullNetApiFactory factory,
        Guid operationId,
        Guid runId,
        Guid approvalId,
        string argumentsJson)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var row = await connection.QuerySingleAsync<(Guid? RunId, string? ArgumentsHash, Guid? ApprovalId)>(
            "SELECT RunId, ArgumentsHash, ApprovalId FROM fn_ai_agent_tool_call WHERE Id = @Id",
            new { Id = operationId }).ConfigureAwait(false);
        Assert.AreEqual(runId, row.RunId);
        Assert.AreEqual(approvalId, row.ApprovalId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(row.ArgumentsHash));
        using var arguments = JsonDocument.Parse(argumentsJson);
        Assert.AreEqual(
            Full.NET.Agents.Approvals.AgentApprovalGate.ComputeArgumentsHash(arguments.RootElement),
            row.ArgumentsHash);
    }
}
