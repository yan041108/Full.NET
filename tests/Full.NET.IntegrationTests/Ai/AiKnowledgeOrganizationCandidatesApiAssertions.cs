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
using Full.NET.Modules.Tenancy.Contracts;

using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库证明可信租户、组织启用状态和资源所有权；模拟目录无法证明这些边界。</summary>
internal static class AiKnowledgeOrganizationCandidatesApiAssertions
{
    private const string BasePermission = "ai.knowledge_bases.organization_candidates_read";
    private const string DocumentPermission = "ai.knowledge_documents.organization_candidates_read";

    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using (var contractClient = factory.CreateClientForHost("localhost"))
            await VerifyOpenApiAsync(contractClient);
        string[] permissions = [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read, AiKnowledgePermissions.Update,
            AiKnowledgePermissions.MembersUpdate, AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Delete,
            BasePermission, DocumentPermission, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"org-owner-{Guid.NewGuid():N}", permissions);
        var member = await factory.CreateHostIdentityAsync($"org-member-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost");
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, member.UserId);
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var hostPath = await CreateBaseAsync(host);
        // Host 没有租户组织；请求租户参数不能把 Host 查询改成租户目录。
        var hostPage = await ReadAsync(host, hostPath + $"/organization-candidates?tenantId={tenant.Id}");
        Assert.HasCount(0, hostPage.Items);
        Assert.AreEqual(0L, hostPage.Total);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
        var path = await CreateBaseAsync(client);
        var first = await InsertUnitAsync(factory, tenant, "candidate-a", true);
        var second = await InsertUnitAsync(factory, tenant, "candidate-b", true, first);
        var disabled = await InsertUnitAsync(factory, tenant, "candidate-disabled", false);
        // 由 Organization 自有 SQL 在另一个可信租户供给行，不能把标识注入当前请求。
        var otherTenant = new TenantSummary(Guid.CreateVersion7(), "foreign", "外部租户", "foreign.localhost", true, 1);
        var foreign = await InsertUnitAsync(factory, otherTenant, "candidate-foreign", true);
        var page = await ReadAsync(client, path + $"/organization-candidates?pageSize=100&tenantId={otherTenant.Id}");
        Assert.IsTrue(page.Items.Any(unit => unit.Id == first));
        Assert.IsTrue(page.Items.Any(unit => unit.Id == second));
        Assert.IsFalse(page.Items.Any(unit => unit.Id == disabled || unit.Id == foreign));
        Assert.AreEqual((long)page.Items.Count, page.Total);
        var ids = new List<Guid>();
        for (var index = 1; index <= page.Total; index++)
        {
            var single = await ReadAsync(client, path + $"/organization-candidates?page={index}&pageSize=1");
            Assert.HasCount(1, single.Items);
            Assert.AreEqual(page.Total, single.Total);
            ids.Add(single.Items[0].Id);
        }
        CollectionAssert.AreEqual(page.Items.Select(unit => unit.Id).ToArray(), ids.ToArray());
        foreach (var invalid in new[] { "page=0", "pageSize=0", "pageSize=101", "page=2147483647&pageSize=100", "page=100002&pageSize=1" })
            await ExpectAsync(client, path + "/organization-candidates?" + invalid, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.PageInvalid);
        Assert.HasCount(0, (await ReadAsync(client, path + "/organization-candidates?page=100001&pageSize=1")).Items);
        using var docCreate = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("组织候选文档", null));
        Assert.AreEqual(HttpStatusCode.Created, docCreate.StatusCode);
        var doc = (await docCreate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var docPath = path + $"/documents/{doc.Id}";
        CollectionAssert.AreEqual(page.Items.Select(unit => unit.Id).ToArray(), (await ReadAsync(client, docPath + "/organization-candidates?pageSize=100")).Items.Select(unit => unit.Id).ToArray());
        using var grant = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode, await grant.Content.ReadAsStringAsync());
        using var reader = factory.CreateClientForHost("acme.localhost");
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, member.AccessToken, tenant.Id));
        await ExpectAsync(reader, path + "/organization-candidates?page=0", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        await ExpectAsync(reader, docPath + "/organization-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var anonymous = factory.CreateClientForHost("acme.localhost");
        await ExpectAsync(anonymous, path + "/organization-candidates", HttpStatusCode.Unauthorized);
        // 切换上下文会更新会话范围，跨范围探针需独立有效 Host 会话。
        var hostReader = await factory.CreateHostIdentityAsync($"org-host-reader-{Guid.NewGuid():N}", permissions);
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostReader.AccessToken);
        await ExpectAsync(host, path + "/organization-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        await ExpectAsync(client, hostPath + "/organization-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        await VerifyIndependentPermissionsAsync(factory, true);
        await VerifyIndependentPermissionsAsync(factory, false);
        await DisableUnitAsync(factory, tenant, first);
        var afterDisable = await ReadAsync(client, path + "/organization-candidates?pageSize=100");
        Assert.IsFalse(afterDisable.Items.Any(unit => unit.Id == first));
        Assert.IsTrue(afterDisable.Items.Any(unit => unit.Id == second));
        Assert.AreEqual(2, (await client.GetFromJsonAsync<AiKnowledgeBaseResponse>(path))!.Version);
        using var disable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("组织候选", null, false, 2));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        await ExpectAsync(client, path + "/organization-candidates", HttpStatusCode.OK);
        await ExpectAsync(client, docPath + "/organization-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var enable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("组织候选", null, true, 3));
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, docPath) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(1)) };
        using var deleted = await client.SendAsync(delete);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        await ExpectAsync(client, docPath + "/organization-candidates", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var finalDocCreate = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("撤权探针", null));
        Assert.AreEqual(HttpStatusCode.Created, finalDocCreate.StatusCode);
        var finalDoc = (await finalDocCreate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        // 保留旧令牌，撤销权威权限后不能继续枚举候选。
        await RevokePermissionsAsync(factory, owner.UserId);
        await ExpectAsync(client, path + "/organization-candidates", HttpStatusCode.Forbidden, "authorization.permission_denied");
        await ExpectAsync(client, path + $"/documents/{finalDoc.Id}/organization-candidates", HttpStatusCode.Forbidden, "authorization.permission_denied");
    }

    private static async Task VerifyOpenApiAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, operationId) in new[]
        {
            ("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/organization-candidates", "aiListKnowledgeOrganizationCandidates"),
            ("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/organization-candidates", "aiListKnowledgeDocumentOrganizationCandidates"),
        })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.AreEqual(operationId, operation.GetProperty("operationId").GetString());
            Assert.IsTrue(operation.GetProperty("security").GetArrayLength() > 0);
            Assert.IsFalse(operation.TryGetProperty("requestBody", out _));
            foreach (var status in new[] { "200", "400", "401", "403", "404" })
                Assert.IsTrue(operation.GetProperty("responses").TryGetProperty(status, out _));
            Assert.IsTrue(operation.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        }
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AiKnowledgeOrganizationCandidateResponse");
        CollectionAssert.AreEquivalent(new[] { "id", "code", "name" }, schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("uuid", schema.GetProperty("properties").GetProperty("id").GetProperty("format").GetString());
    }

    private static async Task VerifyIndependentPermissionsAsync(FullNetApiFactory factory, bool baseOnly)
    {
        var identity = await factory.CreateHostIdentityAsync($"org-limited-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Create, baseOnly ? BasePermission : DocumentPermission]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        var path = await CreateBaseAsync(client);
        using var created = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("独立组织候选权限", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var doc = (await created.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        await ExpectAsync(client, path + "/organization-candidates", baseOnly ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await ExpectAsync(client, path + $"/documents/{doc.Id}/organization-candidates", baseOnly ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
    }

    private static async Task<string> CreateBaseAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("组织候选", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        return $"/api/v1/ai/knowledge-bases/{(await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!.Id}";
    }

    private static async Task<PagedResult<AiKnowledgeOrganizationCandidateResponse>> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResult<AiKnowledgeOrganizationCandidateResponse>>())!;
    }

    private static async Task<Guid> InsertUnitAsync(FullNetApiFactory factory, TenantSummary tenant, string code, bool active, Guid? parent = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenant.Id, tenant.Identifier, tenant.Name));
        try
        {
            var id = Guid.CreateVersion7();
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.organization_candidate.insert", "INSERT INTO fn_organization_unit (Id, TenantId, ParentId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, Version) VALUES (@Id, @TenantId, @ParentId, @Code, @Name, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                IdentitySqlParameters.Create(("Id", id), ("ParentId", parent), ("Code", code), ("Name", "组织候选"),
                    ("DisplayOrder", 0), ("IsActive", active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1))));
            return id;
        }
        finally { current.Clear(); }
    }

    private static async Task DisableUnitAsync(FullNetApiFactory factory, TenantSummary tenant, Guid unitId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenant.Id, tenant.Identifier, tenant.Name));
        try { Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.organization_candidate.disable", "UPDATE fn_organization_unit SET IsActive = 0 WHERE TenantId = @TenantId AND Id = @UnitId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            IdentitySqlParameters.Create(("UnitId", unitId), ("UpdatedAtUtc", DateTimeOffset.UtcNow)))); }
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
            foreach (var roleId in await query.QueryAsync<Guid>(IdentitySql.GetUserAssignableRoleIds, IdentitySqlParameters.Create(("UserId", userId))))
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
