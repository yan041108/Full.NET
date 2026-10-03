using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>通过真实 HTTP 验证候选查询，不能以模拟目录代替数据库授权边界。</summary>
internal static class AiKnowledgeRoleCandidatesApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read, AiKnowledgePermissions.Update,
            AiKnowledgePermissions.MembersUpdate, AiKnowledgePermissions.RoleCandidatesRead, AiKnowledgeDocumentPermissions.RoleCandidatesRead,
            AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Delete, AiKnowledgeDocumentPermissions.MembersUpdate,
            "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"role-candidate-{Guid.NewGuid():N}", permissions);
        var member = await factory.CreateHostIdentityAsync($"role-member-{Guid.NewGuid():N}", permissions);
        var limited = await factory.CreateHostIdentityAsync($"role-limited-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Create, AiKnowledgePermissions.RoleCandidatesRead]);
        var documentOnly = await factory.CreateHostIdentityAsync($"role-document-only-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.RoleCandidatesRead]);
        using var client = factory.CreateClientForHost("localhost");
        using var reader = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("角色候选", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        using var candidates = await client.GetAsync(path + "/role-candidates");
        Assert.AreEqual(HttpStatusCode.OK, candidates.StatusCode, await candidates.Content.ReadAsStringAsync());
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var hostRole = await InsertRoleAsync(factory, null, true, false);
        var inactiveRole = await InsertRoleAsync(factory, null, false, false);
        var superRole = await InsertRoleAsync(factory, null, true, true);
        var tenantRole = await InsertRoleAsync(factory, tenant.Id, true, false);
        var inactiveTenantRole = await InsertRoleAsync(factory, tenant.Id, false, false);
        var otherTenantRole = await InsertRoleAsync(factory, Guid.CreateVersion7(), true, false);
        var page = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(path + "/role-candidates?pageSize=100"))!;
        Assert.IsTrue(page.Items.Any(role => role.Id == hostRole));
        Assert.IsFalse(page.Items.Any(role => new[] { inactiveRole, superRole, tenantRole, inactiveTenantRole, otherTenantRole }.Contains(role.Id)));
        Assert.AreEqual(page.Items.Count, page.Total);
        var ids = new List<Guid>();
        for (var index = 1; index <= page.Total; index++)
        {
            var single = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(path + $"/role-candidates?page={index}&pageSize=1"))!;
            Assert.AreEqual(page.Total, single.Total);
            Assert.HasCount(1, single.Items);
            ids.Add(single.Items[0].Id);
        }
        CollectionAssert.AreEqual(page.Items.Select(role => role.Id).ToArray(), ids.ToArray());
        Assert.HasCount(0, (await client.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(path + "/role-candidates?page=100001&pageSize=1"))!.Items);
        foreach (var invalid in new[] { "page=0", "pageSize=0", "pageSize=101", "page=2147483647&pageSize=100", "page=100002&pageSize=1" })
            await ExpectAsync(client, path + "/role-candidates?" + invalid, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.PageInvalid);
        // 客户端的租户参数不能改变由认证管线建立的范围。
        var injected = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(path + $"/role-candidates?tenantId={tenant.Id}&pageSize=100"))!;
        CollectionAssert.AreEqual(page.Items.Select(role => role.Id).ToArray(), injected.Items.Select(role => role.Id).ToArray());
        using var grant = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode);
        using var createDocument = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("候选文档", null));
        Assert.AreEqual(HttpStatusCode.Created, createDocument.StatusCode);
        var document = (await createDocument.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var docPath = path + $"/documents/{document.Id}";
        await ExpectAsync(client, docPath + "/role-candidates", HttpStatusCode.OK);
        await ExpectAsync(client, path + $"/documents/{Guid.CreateVersion7()}/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        await ExpectAsync(reader, path + "/role-candidates?page=0", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        await ExpectAsync(reader, docPath + "/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var anonymous = factory.CreateClientForHost("localhost");
        await ExpectAsync(anonymous, path + "/role-candidates", HttpStatusCode.Unauthorized);
        await VerifyIndependentPermissionsAsync(reader, limited.AccessToken, true);
        await VerifyIndependentPermissionsAsync(reader, documentOnly.AccessToken, false);
        // 候选只读，不增加目录或文档版本；禁用目录后仅知识库所有者仍可配置。
        Assert.AreEqual(2, (await client.GetFromJsonAsync<AiKnowledgeBaseResponse>(path))!.Version);
        using var disable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("角色候选", null, false, 2));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        await ExpectAsync(client, path + "/role-candidates", HttpStatusCode.OK);
        await ExpectAsync(client, docPath + "/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var enable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("角色候选", null, true, 3));
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, docPath) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(1)) };
        using var deleted = await client.SendAsync(delete);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        await ExpectAsync(client, docPath + "/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        await DisableRoleAsync(factory, hostRole);
        Assert.IsFalse((await client.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(path + "/role-candidates?pageSize=100"))!.Items.Any(role => role.Id == hostRole));
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, owner.AccessToken, tenant.Id);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        await ExpectAsync(tenantClient, path + "/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var tenantCreate = await tenantClient.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户角色候选", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantCreate.StatusCode);
        var tenantBase = (await tenantCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var tenantPath = $"/api/v1/ai/knowledge-bases/{tenantBase.Id}";
        var tenantCandidates = (await tenantClient.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(tenantPath + "/role-candidates?pageSize=100"))!;
        CollectionAssert.AreEqual(new[] { tenantRole }, tenantCandidates.Items.Select(role => role.Id).ToArray());
        using var tenantDocCreate = await tenantClient.PostAsJsonAsync(tenantPath + "/documents", new CreateAiKnowledgeDocumentRequest("租户文档", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantDocCreate.StatusCode);
        var tenantDoc = (await tenantDocCreate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var tenantDocCandidates = (await tenantClient.GetFromJsonAsync<PagedResult<AiKnowledgeRoleCandidateResponse>>(tenantPath + $"/documents/{tenantDoc.Id}/role-candidates"))!;
        CollectionAssert.AreEqual(new[] { tenantRole }, tenantDocCandidates.Items.Select(role => role.Id).ToArray());
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limited.AccessToken);
        await ExpectAsync(reader, tenantPath + "/role-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        // 使用已签发的旧令牌继续请求，Endpoint 权限撤销必须从权威源立即拒绝。
        await RevokePermissionsAsync(factory, owner.UserId);
        await ExpectAsync(tenantClient, tenantPath + "/role-candidates", HttpStatusCode.Forbidden, "authorization.permission_denied");
        await ExpectAsync(tenantClient, tenantPath + $"/documents/{tenantDoc.Id}/role-candidates", HttpStatusCode.Forbidden, "authorization.permission_denied");
    }

    private static async Task VerifyIndependentPermissionsAsync(HttpClient client, string token, bool baseOnly)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("独立权限", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        using var docCreated = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("独立权限文档", null));
        Assert.AreEqual(HttpStatusCode.Created, docCreated.StatusCode);
        var document = (await docCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        await ExpectAsync(client, path + "/role-candidates", baseOnly ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            baseOnly ? null : "authorization.permission_denied");
        await ExpectAsync(client, path + $"/documents/{document.Id}/role-candidates", baseOnly ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
            baseOnly ? "authorization.permission_denied" : null);
    }

    // 仅测试夹具通过 Identity 自有语句供给目录，生产 AI 不接触 Identity 表。
    private static async Task<Guid> InsertRoleAsync(FullNetApiFactory factory, Guid? tenantId, bool active, bool super)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        try
        {
            var id = Guid.CreateVersion7();
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(IdentitySql.InsertRole,
                IdentitySqlParameters.Create(("Id", id), ("TenantId", tenantId), ("ScopeKey", tenantId is { } tenant ? $"tenant:{tenant:N}" : "host"),
                    ("Code", $"candidate-{id:N}"), ("Name", "候选角色"), ("IsSystem", super), ("IsActive", active),
                    ("IsSuperAdministrator", super), ("DataScopeKind", "all"), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1))));
            return id;
        }
        finally { current.Clear(); }
    }

    private static async Task DisableRoleAsync(FullNetApiFactory factory, Guid roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        try
        {
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(IdentitySql.DisableHostRole,
                IdentitySqlParameters.Create(("RoleId", roleId), ("UpdatedAtUtc", DateTimeOffset.UtcNow))));
        }
        finally { current.Clear(); }
    }

    private static async Task RevokePermissionsAsync(FullNetApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        try
        {
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var roles = await query.QueryAsync<Guid>(IdentitySql.GetUserAssignableRoleIds, IdentitySqlParameters.Create(("UserId", userId)));
            foreach (var roleId in roles)
                await command.ExecuteAsync(IdentitySql.DeleteRolePermissions, IdentitySqlParameters.Create(("RoleId", roleId)));
        }
        finally { current.Clear(); }
    }

    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode status, string? code = null)
    {
        using var response = await client.GetAsync(path);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        if (code is null) return;
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }
}
