using System.Net;
using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用独立数据库夹具；真实权限中间件及 SQL 并发不能由纯单元测试证明。</summary>
internal static class AiKnowledgeBaseApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync($"knowledge-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update, AiKnowledgePermissions.PolicyUpdate,
             AiModelPermissions.Read, AiModelPermissions.Create, "tenancy.tenants.switch"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        using var response = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("制度", null));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var knowledgeBase = (await response.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        Assert.AreEqual("internal", knowledgeBase.DataClassification);
        Assert.IsNull(knowledgeBase.EmbeddingModelConfigId);
        Assert.IsNull(knowledgeBase.GenerationModelConfigId);
        var path = $"/api/v1/ai/knowledge-bases/{knowledgeBase.Id:D}";
        Assert.IsNull(knowledgeBase.EmbeddingModelVersion);
        Assert.IsNull(knowledgeBase.GenerationModelVersion);
        Assert.IsTrue(knowledgeBase.IsEnabled);
        Assert.AreEqual(1, knowledgeBase.Version);
        using var invalid = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest(" ", null));
        await ProblemAsync(invalid, HttpStatusCode.BadRequest, "ai.knowledge.input_invalid");
        using (var localized = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()))
            Assert.AreEqual("知识库目录或审批请求无效。", localized.RootElement.GetProperty("title").GetString());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        using var pageInvalid = await client.GetAsync("/api/v1/ai/knowledge-bases?page=2147483647&pageSize=100");
        await ProblemAsync(pageInvalid, HttpStatusCode.BadRequest, "ai.knowledge.page_invalid");
        using (var localized = JsonDocument.Parse(await pageInvalid.Content.ReadAsStringAsync()))
            Assert.AreEqual("Page size must be 1..100 and offset must not exceed 100000.", localized.RootElement.GetProperty("title").GetString());
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        using var injected = await client.PostAsync("/api/v1/ai/knowledge-bases", new StringContent(
            $"{{\"name\":\"注入\",\"tenantId\":\"{Guid.CreateVersion7()}\"}}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);

        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "审批模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var disabledResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "禁用审批模型", "ollama", "https://provider.test", "model", null, null, false, false));
        Assert.AreEqual(HttpStatusCode.Created, disabledResponse.StatusCode);
        var disabledModel = (await disabledResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var foreignResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "其他租户模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, foreignResponse.StatusCode);
        var foreignModel = (await foreignResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        // 只供隔离断言的其他租户模型：不调用 Provider，不依赖另一模块的表或外键。
        await using (DbConnection connection = factory.Provider == DatabaseProvider.SqlServer ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(factory.ConnectionString) { GuidFormat = MySqlGuidFormat.Binary16 }.ConnectionString))
            Assert.AreEqual(1, await connection.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId = @TenantId WHERE Id = @Id",
                new { TenantId = Guid.CreateVersion7(), Id = foreignModel.Id }));
        var policy = new UpdateAiKnowledgePolicyRequest("restricted", model.Id, model.Version, model.Id, model.Version, knowledgeBase.Version);
        using var invalidPair = await client.PutAsJsonAsync(path + "/policy", policy with { EmbeddingModelVersion = null });
        await ProblemAsync(invalidPair, HttpStatusCode.BadRequest, "ai.knowledge.input_invalid");
        using var drift = await client.PutAsJsonAsync(path + "/policy", policy with { EmbeddingModelVersion = model.Version + 1 });
        await ProblemAsync(drift, HttpStatusCode.UnprocessableEntity, "ai.knowledge.model_unavailable");
        using var acceptedPolicy = await client.PutAsJsonAsync(path + "/policy", policy);
        Assert.AreEqual(HttpStatusCode.OK, acceptedPolicy.StatusCode, await acceptedPolicy.Content.ReadAsStringAsync());
        knowledgeBase = (await acceptedPolicy.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        Assert.AreEqual(model.Id, knowledgeBase.EmbeddingModelConfigId);
        Assert.AreEqual(model.Version, knowledgeBase.GenerationModelVersion);
        Assert.AreEqual(2, knowledgeBase.Version);
        using var stalePolicy = await client.PutAsJsonAsync(path + "/policy", policy);
        await ProblemAsync(stalePolicy, HttpStatusCode.Conflict, "ai.knowledge.version_conflict");

        // 两个并发编辑共享版本；只能有一个成功，且不会改写模型审批。
        var edits = await Task.WhenAll(
            client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("制度甲", "说明", false, knowledgeBase.Version)),
            client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("制度乙", "说明", false, knowledgeBase.Version)));
        try
        {
            Assert.AreEqual(1, edits.Count(edit => edit.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, edits.Count(edit => edit.StatusCode == HttpStatusCode.Conflict));
            knowledgeBase = (await edits.Single(edit => edit.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            Assert.AreEqual(model.Id, knowledgeBase.EmbeddingModelConfigId);
            Assert.AreEqual("restricted", knowledgeBase.DataClassification);
            Assert.IsFalse(knowledgeBase.IsEnabled);
        }
        finally { foreach (var edit in edits) edit.Dispose(); }
        using var revoked = await client.PutAsJsonAsync(path + "/policy", new UpdateAiKnowledgePolicyRequest("internal", null, null, null, null, knowledgeBase.Version));
        Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode);
        var cleared = (await revoked.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        Assert.IsNull(cleared.EmbeddingModelConfigId);
        Assert.IsNull(cleared.GenerationModelVersion);

        // 精确权限也不能获得另一所有者的资源，更新统一返回 404。
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync(
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update, AiKnowledgePermissions.PolicyUpdate]));
        using var otherRead = await client.GetAsync(path);
        await ProblemAsync(otherRead, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var otherEdit = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("越权", null, true, cleared.Version));
        await ProblemAsync(otherEdit, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var otherPolicy = await client.PutAsJsonAsync(path + "/policy", policy with { Version = cleared.Version });
        await ProblemAsync(otherPolicy, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        var empty = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!;
        Assert.AreEqual(0, empty.Items.Count);

        // 普通编辑者创建的私有目录仍须独立的审批权限；不能把字段塞入普通更新。
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateHostAccessTokenAsync(
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update]));
        using var editorCreate = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("编辑者目录", null));
        Assert.AreEqual(HttpStatusCode.Created, editorCreate.StatusCode);
        var editorBase = (await editorCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        using var deniedPolicy = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{editorBase.Id}/policy", policy with { Version = 1 });
        await ProblemAsync(deniedPolicy, HttpStatusCode.Forbidden, "authorization.permission_denied");
        using var injectedApproval = await client.PutAsync($"/api/v1/ai/knowledge-bases/{editorBase.Id}", new StringContent(
            $"{{\"name\":\"编辑者\",\"description\":null,\"isEnabled\":true,\"version\":1,\"embeddingModelConfigId\":\"{model.Id}\"}}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, injectedApproval.StatusCode);

        // 同一 Host 主体切到真实租户后也看不到 Host 目录，租户写入必须绑定可信 TenantId。
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var currentTenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, identity.AccessToken, currentTenant.Id);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var hostHidden = await tenantClient.GetAsync(path);
        await ProblemAsync(hostHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var hostEditHidden = await tenantClient.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("跨范围", null, true, cleared.Version));
        await ProblemAsync(hostEditHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var hostPolicyHidden = await tenantClient.PutAsJsonAsync(path + "/policy", policy with { Version = cleared.Version });
        await ProblemAsync(hostPolicyHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var tenantCreate = await tenantClient.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户目录", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantCreate.StatusCode, await tenantCreate.Content.ReadAsStringAsync());
        var tenantBase = (await tenantCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        foreach (var unavailable in new[] { disabledModel, foreignModel })
        {
            using var rejectedPolicy = await tenantClient.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}/policy",
                policy with { EmbeddingModelConfigId = unavailable.Id, EmbeddingModelVersion = unavailable.Version, Version = 1 });
            await ProblemAsync(rejectedPolicy, HttpStatusCode.UnprocessableEntity, "ai.knowledge.model_unavailable");
            var unchanged = (await tenantClient.GetFromJsonAsync<AiKnowledgeBaseResponse>($"/api/v1/ai/knowledge-bases/{tenantBase.Id}"))!;
            Assert.AreEqual(1, unchanged.Version);
            Assert.IsNull(unchanged.EmbeddingModelConfigId);
        }
        using var tenantApproval = await tenantClient.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}/policy", policy with { Version = 1 });
        Assert.AreEqual(HttpStatusCode.OK, tenantApproval.StatusCode, await tenantApproval.Content.ReadAsStringAsync());
        var tenantPage = (await tenantClient.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!;
        Assert.HasCount(1, tenantPage.Items);
        Assert.AreEqual(tenantBase.Id, tenantPage.Items[0].Id);
        using var switchBack = await tenantClient.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        Assert.AreEqual(HttpStatusCode.OK, switchBack.StatusCode, await switchBack.Content.ReadAsStringAsync());
        var hostContext = (await switchBack.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostContext.AccessToken);
        using var tenantHidden = await client.GetAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}");
        await ProblemAsync(tenantHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var tenantEditHidden = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}", new UpdateAiKnowledgeBaseRequest("跨范围", null, true, 2));
        await ProblemAsync(tenantEditHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        using var tenantPolicyHidden = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}/policy", policy with { Version = 2 });
        await ProblemAsync(tenantPolicyHidden, HttpStatusCode.NotFound, "ai.knowledge.not_found");
        var hostPage = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!;
        Assert.HasCount(1, hostPage.Items);
        Assert.AreEqual(knowledgeBase.Id, hostPage.Items[0].Id);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, json.RootElement.GetProperty("code").GetString());
    }
}
