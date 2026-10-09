using System.Net;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.IntegrationTests.Api;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用真实双库 Agent 夹具，验证 Prompt 发布及运行绑定的实际 HTTP 边界。</summary>
internal static class AiPromptApiAssertions
{
    internal static async Task VerifyAsync(HttpClient client, Guid modelId)
    {
        using var list = await client.GetAsync("/api/v1/ai/prompts?page=1&pageSize=20");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());
        var request = new CreateAiPromptRequest(Guid.CreateVersion7(), "Prompt 集成验证", new(modelId, "zh-CN", "第一版 {{input}}", 100, 100));
        var head = await HeadAsync(client, "/api/v1/ai/prompts", request, HttpStatusCode.Created);
        Assert.IsNull(head.ActiveVersionId); Assert.AreEqual(1, head.Revision); Assert.AreEqual(1, head.LastVersion);
        var retry = await HeadAsync(client, "/api/v1/ai/prompts", request, HttpStatusCode.Created);
        Assert.AreEqual(head.Id, retry.Id);
        await RejectedAsync(client, "/api/v1/ai/prompts", request with { Name = "不同载荷" }, HttpStatusCode.Conflict, "ai.prompts.conflict");
        await RejectedAsync(client, "/api/v1/ai/prompts", request with { ClientRequestId = Guid.CreateVersion7(), Body = request.Body with { Template = "{{input}}{{input}}" } }, HttpStatusCode.BadRequest, "ai.prompts.invalid");
        var root = $"/api/v1/ai/prompts/{head.Id}";
        var v1 = (await VersionsAsync(client, root)).Single();
        Assert.IsFalse(v1.WasPublished); Assert.AreEqual(64, v1.BodyHash.Length); Assert.HasCount(0, v1.AllowedToolNames);
        await RejectedAsync(client, root + "/rollback", new SelectAiPromptVersionRequest(1, v1.Id), HttpStatusCode.BadRequest, "ai.prompts.invalid");
        head = await HeadAsync(client, root + "/publish", new SelectAiPromptVersionRequest(1, v1.Id));
        Assert.AreEqual(v1.Id, head.ActiveVersionId);
        var publishRetry = await HeadAsync(client, root + "/publish", new SelectAiPromptVersionRequest(1, v1.Id));
        Assert.AreEqual(head.Revision, publishRetry.Revision);
        head = await HeadAsync(client, root + "/versions", new AppendAiPromptVersionRequest(head.Revision, request.Body with { Template = "第二版 {{input}}" }));
        Assert.AreEqual(2, head.LastVersion); Assert.AreEqual(v1.Id, head.ActiveVersionId);
        var versions = await VersionsAsync(client, root); var v2 = versions.Single(v => v.Version == 2);
        Assert.AreEqual(v1.BodyHash, versions.Single(v => v.Version == 1).BodyHash);
        await RejectedAsync(client, root + "/versions", new AppendAiPromptVersionRequest(1, request.Body), HttpStatusCode.Conflict, "ai.prompts.conflict");
        await RejectedAsync(client, root + "/rollback", new SelectAiPromptVersionRequest(head.Revision, v2.Id), HttpStatusCode.BadRequest, "ai.prompts.invalid");
        head = await HeadAsync(client, root + "/publish", new SelectAiPromptVersionRequest(head.Revision, v2.Id));
        // 发布权限不能选择曾发布旧版；回滚必须经过独立端点和精确权限。
        await RejectedAsync(client, root + "/publish", new SelectAiPromptVersionRequest(head.Revision, v1.Id), HttpStatusCode.BadRequest, "ai.prompts.invalid");
        head = await HeadAsync(client, root + "/rollback", new SelectAiPromptVersionRequest(head.Revision, v1.Id));
        Assert.AreEqual(v1.Id, head.ActiveVersionId);
        var deactivateRevision = head.Revision;
        head = await HeadAsync(client, root + "/deactivate", new DeactivateAiPromptRequest(deactivateRevision));
        Assert.IsNull(head.ActiveVersionId); Assert.AreEqual(1, head.Epoch);
        var deactivateRetry = await HeadAsync(client, root + "/deactivate", new DeactivateAiPromptRequest(deactivateRevision));
        Assert.AreEqual(head.Revision, deactivateRetry.Revision); Assert.AreEqual(1, deactivateRetry.Epoch);
        head = await HeadAsync(client, root + "/rollback", new SelectAiPromptVersionRequest(head.Revision, v1.Id));
        Assert.AreEqual(1, head.Epoch); Assert.AreEqual(v1.Id, head.ActiveVersionId);
        // 两个目录不能互相发布对方版本。
        var other = await HeadAsync(client, "/api/v1/ai/prompts", request with { ClientRequestId = Guid.CreateVersion7(), Name = "其他目录" }, HttpStatusCode.Created);
        await RejectedAsync(client, $"/api/v1/ai/prompts/{other.Id}/publish", new SelectAiPromptVersionRequest(1, v1.Id), HttpStatusCode.NotFound, "ai.prompts.not_found");
    }

    internal static async Task VerifyRunAsync(HttpClient client, Guid modelId, FullNetApiFactory factory)
    {
        var head = await HeadAsync(client, "/api/v1/ai/prompts", new CreateAiPromptRequest(Guid.CreateVersion7(), "绑定 Run", new(modelId, "en-US", "Bound {{input}}", 100, 100)), HttpStatusCode.Created);
        var root = $"/api/v1/ai/prompts/{head.Id}"; var version = (await VersionsAsync(client, root)).Single();
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", modelId, "raw input", 100, 100, version.Id);
        await RejectedAsync(client, "/api/v1/ai/agent/runs", request, HttpStatusCode.NotFound, "ai.prompts.unavailable");
        head = await HeadAsync(client, root + "/publish", new SelectAiPromptVersionRequest(1, version.Id));
        using var response = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;
        await using var db = Full.NET.IntegrationTests.Migrations.Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(factory.Provider == Full.NET.Data.Abstractions.DatabaseProvider.SqlServer, factory.ConnectionString);
        using var snapshot = JsonDocument.Parse(await db.QuerySingleAsync<string>("SELECT BudgetJson FROM fn_ai_agent_run WHERE Id=@Id", new { Id = created.RunId }));
        // 持久化快照沿用源生成上下文的 PascalCase；HTTP 的 camelCase 不改变内部运行协议。
        Assert.AreEqual(request.Prompt, snapshot.RootElement.GetProperty("Prompt").GetString());
        var binding = snapshot.RootElement.GetProperty("PromptBinding");
        Assert.AreEqual(version.Id, binding.GetProperty("VersionId").GetGuid()); Assert.AreEqual(version.BodyHash, binding.GetProperty("BodyHash").GetString()); Assert.AreEqual(0, binding.GetProperty("Epoch").GetInt32());
        using var limitedClient = factory.CreateClientForHost("localhost");
        var limited = await factory.CreateHostIdentityAsync($"prompt-no-use-{Guid.CreateVersion7():N}", [AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiPromptPermissions.Read]);
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limited.AccessToken);
        using var forbidden = await limitedClient.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { ClientRequestId = Guid.CreateVersion7() });
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var retry = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", request);
        Assert.AreEqual(HttpStatusCode.Accepted, retry.StatusCode); Assert.AreEqual(created.RunId, (await retry.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId);
        await RejectedAsync(client, "/api/v1/ai/agent/runs", request with { Prompt = "changed" }, HttpStatusCode.Conflict, "ai.prompts.conflict");
        await RejectedAsync(client, "/api/v1/ai/agent/runs", request with { PromptVersionId = null }, HttpStatusCode.Conflict, "ai.prompts.conflict");
        await RejectedAsync(client, "/api/v1/ai/agent/runs", request with { ClientRequestId = Guid.CreateVersion7(), InputTokenLimit = 101 }, HttpStatusCode.BadRequest, "ai.prompts.invalid");
        // 混合载荷并发竞争只接受一个 Run；落败预留不保持 reserved。
        var key = Guid.CreateVersion7();
        var competing = await Task.WhenAll(client.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { ClientRequestId = key }),
            client.PostAsJsonAsync("/api/v1/ai/agent/runs", request with { ClientRequestId = key, PromptVersionId = null }));
        try
        {
            Assert.AreEqual(1, competing.Count(r => r.StatusCode == HttpStatusCode.Accepted));
            Assert.AreEqual(1, competing.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var winner = (await competing.Single(r => r.StatusCode == HttpStatusCode.Accepted).Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;
            using var stopWinner = await client.PostAsync($"/api/v1/ai/agent/runs/{winner.RunId}/cancel", null); Assert.AreEqual(HttpStatusCode.OK, stopWinner.StatusCode);
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE UsageStatus='reserved'"));
        }
        finally { foreach (var item in competing) item.Dispose(); }
        await AiPromptCreationRaceAssertions.VerifyAsync(factory, db, modelId, version.Id, created.RunId);
        head = await HeadAsync(client, root + "/deactivate", new DeactivateAiPromptRequest(head.Revision));
        await RejectedAsync(client, "/api/v1/ai/agent/runs", request with { ClientRequestId = Guid.CreateVersion7() }, HttpStatusCode.NotFound, "ai.prompts.unavailable");
        using var cancel = await client.PostAsync($"/api/v1/ai/agent/runs/{created.RunId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
    }

    internal static async Task VerifyTenantAsync(HttpClient hostClient, Guid modelId, FullNetApiFactory factory, string accessToken)
    {
        var request = new CreateAiPromptRequest(Guid.CreateVersion7(), "scope", new(modelId, "en-US", "{{input}}", 100, 100));
        var host = await HeadAsync(hostClient, "/api/v1/ai/prompts", request, HttpStatusCode.Created);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(hostClient, accessToken, tenant.Id);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var crossRead = await tenantClient.GetAsync($"/api/v1/ai/prompts/{host.Id}/versions");
        Assert.AreEqual(HttpStatusCode.NotFound, crossRead.StatusCode);
        using var crossWrite = await tenantClient.PostAsJsonAsync($"/api/v1/ai/prompts/{host.Id}/deactivate", new DeactivateAiPromptRequest(1));
        Assert.AreEqual(HttpStatusCode.NotFound, crossWrite.StatusCode);
        var scoped = await HeadAsync(tenantClient, "/api/v1/ai/prompts", request, HttpStatusCode.Created);
        Assert.AreNotEqual(host.Id, scoped.Id);
        using var switchBack = await tenantClient.PutAsJsonAsync("/api/v1/tenancy/context", new Full.NET.Modules.Tenancy.Contracts.ChangeTenantContextRequest(null));
        Assert.AreEqual(HttpStatusCode.OK, switchBack.StatusCode);
        var restored = (await switchBack.Content.ReadFromJsonAsync<Full.NET.Modules.Identity.Contracts.TenantContextTokenResponse>())!;
        hostClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restored.AccessToken);
        using var hostRead = await hostClient.GetAsync($"/api/v1/ai/prompts/{scoped.Id}/versions");
        Assert.AreEqual(HttpStatusCode.NotFound, hostRead.StatusCode);
    }

    private static async Task<IReadOnlyList<AiPromptVersionResponse>> VersionsAsync(HttpClient client, string root)
    {
        using var response = await client.GetAsync(root + "/versions?page=1&pageSize=100");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResult<AiPromptVersionResponse>>())!.Items;
    }
    private static async Task<AiPromptResponse> HeadAsync<T>(HttpClient client, string url, T request, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(url, request);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AiPromptResponse>())!;
    }
    private static async Task RejectedAsync<T>(HttpClient client, string url, T request, HttpStatusCode status, string code)
    {
        using var response = await client.PostAsJsonAsync(url, request);
        await AiAgentRunApiAssertions.AssertProblemAsync(response, status, code);
    }
}
