using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证明确机构授予、两层授权与旧令牌即时撤权。</summary>
internal static class AiKnowledgeOrganizationGrantsApiAssertions
{
    private const string BaseRead = "ai.knowledge_bases.organizations_read";
    private const string BaseWrite = "ai.knowledge_bases.organizations_update";
    private const string DocRead = "ai.knowledge_documents.organizations_read";
    private const string DocWrite = "ai.knowledge_documents.organizations_update";
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeOrganizationGrantFaults faults)
    {
        await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read, AiKnowledgePermissions.Update,
            AiKnowledgePermissions.MembersUpdate, AiKnowledgePermissions.RolesUpdate,
            AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Delete,
            AiKnowledgeDocumentPermissions.MembersUpdate, AiKnowledgeDocumentPermissions.RolesUpdate, AiKnowledgeDocumentPermissions.Upload,
            AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
            "tenancy.tenants.switch", BaseRead, BaseWrite, DocRead, DocWrite];
        var ownerIdentity = await factory.CreateHostIdentityAsync($"org-grant-owner-{Guid.NewGuid():N}", permissions);
        var readerIdentity = await factory.CreateHostIdentityAsync($"org-grant-reader-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost");
        using var hostReader = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerIdentity.AccessToken);
        hostReader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerIdentity.AccessToken);
        var hostPath = await CreateBaseAsync(host);
        await ExpectAsync(host, hostPath + "/organizations", HttpStatusCode.OK);
        await SetAsync(host, hostPath, [Guid.CreateVersion7()], 1, HttpStatusCode.UnprocessableEntity);
        await SetAsync(host, hostPath, [], 1, HttpStatusCode.OK);
        using var owner = factory.CreateClientForHost("acme.localhost");
        using var reader = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(owner);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, ownerIdentity.UserId);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, readerIdentity.UserId);
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, ownerIdentity.AccessToken, tenant.Id));
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(hostReader, readerIdentity.AccessToken, tenant.Id));
        var parent = await UnitAsync(factory, tenant, true);
        var child = await UnitAsync(factory, tenant, true, parent);
        var inactive = await UnitAsync(factory, tenant, false);
        var foreignTenant = tenant with { Id = Guid.CreateVersion7(), Identifier = "foreign" };
        var foreign = await UnitAsync(factory, foreignTenant, true);
        await MembershipAsync(factory, tenant, child, readerIdentity.UserId, insert: true);
        await MembershipAsync(factory, foreignTenant, foreign, readerIdentity.UserId, insert: true);
        // 旧结构只以 UnitId 建外键，模拟机构与隶属 TenantId 不一致的残留关系。
        await MembershipAsync(factory, tenant, foreign, readerIdentity.UserId, insert: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            current.SetTenant(new TenantContext(tenant.Id, tenant.Identifier, tenant.Name));
            try
            {
                var active = await scope.ServiceProvider.GetRequiredService<ICurrentTenantOrganizationUnitDirectory>().ListActiveUnitIdsForUserAsync(readerIdentity.UserId);
                Assert.IsTrue(active.Contains(child)); Assert.IsFalse(active.Contains(foreign));
            }
            finally { current.Clear(); }
        }
        var path = await CreateBaseAsync(owner);
        using var created = await owner.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("机构授权文档", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var document = (await created.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var docPath = path + $"/documents/{document.Id}";
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await SetAsync(owner, path, [parent], 1, HttpStatusCode.OK);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound, "父机构授予不能隐含子机构成员资格。");
        await SetAsync(owner, path, [child], 2, HttpStatusCode.OK);
        await ExpectAsync(reader, path, HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await SetAsync(owner, docPath, [child], 1, HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await ExpectAsync(reader, path + "/organizations", HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath + "/organizations", HttpStatusCode.NotFound);
        using (var page = JsonDocument.Parse(await reader.GetStringAsync(path + "/documents?pageSize=1")))
        { Assert.AreEqual(1, page.RootElement.GetProperty("total").GetInt32()); Assert.AreEqual(document.Id, page.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid()); }
        // 旧名单与角色写入只修改各自授予，不能清空机构范围。
        using (var members = await owner.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], 3))) Assert.AreEqual(HttpStatusCode.OK, members.StatusCode);
        using (var roles = await owner.PutAsJsonAsync(path + "/roles", new SetAiKnowledgeRoleGrantsRequest([], 4))) Assert.AreEqual(HttpStatusCode.OK, roles.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await SetAsync(owner, path, [], 4, HttpStatusCode.Conflict);
        foreach (var ids in new Guid[][] { [Guid.Empty], [child, child], Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray() })
            await SetAsync(owner, path, ids, 5, HttpStatusCode.BadRequest);
        foreach (var unit in new[] { foreign, inactive, Guid.CreateVersion7() }) await SetAsync(owner, path, [unit], 5, HttpStatusCode.UnprocessableEntity);
        await SetAsync(owner, path, [], 0, HttpStatusCode.BadRequest);
        using (var injected = await owner.PutAsync(path + "/organizations", new StringContent("{\"organizationUnitIds\":[],\"version\":5,\"tenantId\":null}", Encoding.UTF8, "application/json")))
            Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);
        await MembershipAsync(factory, tenant, child, readerIdentity.UserId, active: false);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await MembershipAsync(factory, tenant, child, readerIdentity.UserId);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await ExecuteAsync(factory, tenant, "UPDATE fn_organization_unit SET IsActive=0 WHERE TenantId=@TenantId AND Id=@UnitId", ("UnitId", child));
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await ExecuteAsync(factory, tenant, "UPDATE fn_organization_unit SET IsActive=1 WHERE TenantId=@TenantId AND Id=@UnitId", ("UnitId", child));
        // 父机构禁用不抹去明确授予的活动子机构，机构自身与成员隶属必须分别有效。
        await ExecuteAsync(factory, tenant, "UPDATE fn_organization_unit SET IsActive=0 WHERE TenantId=@TenantId AND Id=@UnitId", ("UnitId", parent));
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await VerifySourceAsync(factory, faults, owner, reader, tenant, child, readerIdentity.UserId, path, docPath);
        // 大量合法机构关系不能受 SQL Server 参数上限或候选页大小截断。
        Guid tail = default;
        for (var index = 0; index < 2105; index++)
        {
            tail = await UnitAsync(factory, tenant, true);
            await MembershipAsync(factory, tenant, tail, readerIdentity.UserId, insert: true);
        }
        await SetAsync(owner, path, [tail], await VersionAsync(owner, path), HttpStatusCode.OK);
        await SetAsync(owner, docPath, [tail], await VersionAsync(owner, docPath), HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK, "完整机构关系中尾部授予必须可见。");
        await SetAsync(owner, path, [child], await VersionAsync(owner, path), HttpStatusCode.OK);
        await SetAsync(owner, docPath, [child], await VersionAsync(owner, docPath), HttpStatusCode.OK);
        await VerifyMixedRoleAccessAsync(factory, owner, reader, tenant, readerIdentity.UserId, child, path, docPath);
        // 每层可以用明确用户与机构交叉匹配，任一层没有匹配就拒读。
        await SetAsync(owner, path, [], await VersionAsync(owner, path), HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        using (var members = await owner.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([readerIdentity.UserId], await VersionAsync(owner, path)))) Assert.AreEqual(HttpStatusCode.OK, members.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await SetAsync(owner, docPath, [], await VersionAsync(owner, docPath), HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        using (var members = await owner.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([readerIdentity.UserId], await VersionAsync(owner, docPath)))) Assert.AreEqual(HttpStatusCode.OK, members.StatusCode);
        await SetAsync(owner, path, [child], await VersionAsync(owner, path), HttpStatusCode.OK);
        using (var members = await owner.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], await VersionAsync(owner, path)))) Assert.AreEqual(HttpStatusCode.OK, members.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        var before = await VersionAsync(owner, path);
        faults.FailInsertAt = 2;
        var another = await UnitAsync(factory, tenant, true);
        await SetAsync(owner, path, [child, another], before, HttpStatusCode.NotFound);
        Assert.AreEqual(before, await VersionAsync(owner, path), "失败必须回滚版本和授予替换。");
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        var writes = await Task.WhenAll(owner.PutAsJsonAsync(path + "/organizations", new { organizationUnitIds = new[] { child }, version = before }),
            owner.PutAsJsonAsync(path + "/organizations", new { organizationUnitIds = new[] { another }, version = before }));
        try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, writes.Select(x => x.StatusCode).ToArray()); }
        finally { foreach (var response in writes) response.Dispose(); }
        await VerifyOpenApiAsync(owner);
        await VerifyPermissionsAsync(factory, true);
        await VerifyPermissionsAsync(factory, false);
        using (var disabled = await owner.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("禁用机构目录", null, false, await VersionAsync(owner, path)))) Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        await ExpectAsync(owner, path + "/organizations", HttpStatusCode.OK);
        await ExpectAsync(owner, docPath + "/organizations", HttpStatusCode.NotFound);
        using (var enabled = await owner.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("机构目录", null, true, await VersionAsync(owner, path)))) Assert.AreEqual(HttpStatusCode.OK, enabled.StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, docPath) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(await VersionAsync(owner, docPath))) };
        using var deleted = await owner.SendAsync(delete);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        await ExpectAsync(owner, docPath + "/organizations", HttpStatusCode.NotFound);
        await RevokePermissionAsync(factory, ownerIdentity.UserId, BaseRead);
        await ExpectAsync(owner, path + "/organizations", HttpStatusCode.Forbidden);
    }
    private static async Task VerifySourceAsync(FullNetApiFactory factory, AiKnowledgeOrganizationGrantFaults faults, HttpClient owner,
        HttpClient reader, TenantSummary tenant, Guid unit, Guid user, string path, string docPath)
    {
        using var upload = await owner.PostAsync(docPath + $"/versions?version=2&requestKey={Guid.NewGuid()}&fileName=source.txt", new StringContent("组织撤权不得泄露来源", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, upload.StatusCode, await upload.Content.ReadAsStringAsync());
        var operation = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        var source = docPath + $"/versions/{operation.VersionId}/source";
        await ExpectAsync(reader, docPath + "/versions", HttpStatusCode.OK);
        await ExpectAsync(reader, source, HttpStatusCode.OK);
        faults.AfterOpen = () => MembershipAsync(factory, tenant, unit, user, active: false);
        await ExpectAsync(reader, source, HttpStatusCode.NotFound);
        Assert.IsFalse(faults.Opened!.CanRead, "来源打开后调离必须释放流。");
        await MembershipAsync(factory, tenant, unit, user);
        faults.AfterOpen = () => ExecuteAsync(factory, tenant, "UPDATE fn_organization_unit SET IsActive=0 WHERE TenantId=@TenantId AND Id=@UnitId", ("UnitId", unit));
        await ExpectAsync(reader, source, HttpStatusCode.NotFound);
        Assert.IsFalse(faults.Opened!.CanRead, "来源打开后机构禁用必须释放流。");
        await ExecuteAsync(factory, tenant, "UPDATE fn_organization_unit SET IsActive=1 WHERE TenantId=@TenantId AND Id=@UnitId", ("UnitId", unit));
        await ExpectAsync(reader, source, HttpStatusCode.OK);
        faults.AfterOpen = () => { faults.FailAuthorityRead = true; return Task.CompletedTask; };
        await ExpectAsync(reader, source, HttpStatusCode.InternalServerError);
        Assert.IsFalse(faults.Opened!.CanRead, "权威机构读取失败也必须释放已打开来源，不能回退旧关系。");
        await ExpectAsync(reader, source, HttpStatusCode.OK);
        faults.AfterOpen = () => { faults.CancelAuthorityRead = true; return Task.CompletedTask; };
        await ExpectAsync(reader, source, HttpStatusCode.InternalServerError);
        Assert.IsFalse(faults.Opened!.CanRead, "权威机构读取取消必须释放已打开来源。");
        await ExpectAsync(reader, source, HttpStatusCode.OK);
    }
    private static async Task VerifyMixedRoleAccessAsync(FullNetApiFactory factory, HttpClient owner, HttpClient reader,
        TenantSummary tenant, Guid user, Guid unit, string path, string docPath)
    {
        var role = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            current.SetHost();
            try
            {
                var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await command.ExecuteAsync(IdentitySql.InsertRole, IdentitySqlParameters.Create(("Id", role), ("TenantId", tenant.Id),
                    ("ScopeKey", $"tenant:{tenant.Id:N}"), ("Code", $"mixed-{role:N}"), ("Name", "混合授权角色"), ("IsSystem", false),
                    ("IsActive", true), ("IsSuperAdministrator", false), ("DataScopeKind", "all"), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)));
            }
            finally { current.Clear(); }
        }
        await RoleMembershipAsync(factory, user, role, false);
        // 知识库机构、文档角色与反向组合都必须独立匹配，两层不能互相替代。
        foreach (var rolePath in new[] { docPath, path })
        {
            await SetAsync(owner, rolePath, [], await VersionAsync(owner, rolePath), HttpStatusCode.OK);
            using (var assigned = await owner.PutAsJsonAsync(rolePath + "/roles", new SetAiKnowledgeRoleGrantsRequest([role], await VersionAsync(owner, rolePath))))
                Assert.AreEqual(HttpStatusCode.OK, assigned.StatusCode, await assigned.Content.ReadAsStringAsync());
            await ExpectAsync(reader, docPath, HttpStatusCode.OK);
            await RoleMembershipAsync(factory, user, role, true);
            await ExpectAsync(reader, docPath, HttpStatusCode.NotFound, "另一层机构授予仍匹配时，旧令牌也必须立即失去被撤角色层的读取资格。");
            await RoleMembershipAsync(factory, user, role, false);
            await ExpectAsync(reader, docPath, HttpStatusCode.OK);
            using (var cleared = await owner.PutAsJsonAsync(rolePath + "/roles", new SetAiKnowledgeRoleGrantsRequest([], await VersionAsync(owner, rolePath))))
                Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
            await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
            await SetAsync(owner, rolePath, [unit], await VersionAsync(owner, rolePath), HttpStatusCode.OK);
        }
    }
    private static async Task RoleMembershipAsync(FullNetApiFactory factory, Guid user, Guid role, bool remove)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        try
        {
            var statement = remove ? new SqlStatement("test.ai.remove_mixed_role", "DELETE FROM fn_identity_user_role WHERE UserId=@UserId AND RoleId=@RoleId", SqlDataScope.HostOnly) : IdentitySql.EnsureUserRole;
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(statement, IdentitySqlParameters.Create(("UserId", user), ("RoleId", role)));
        }
        finally { current.Clear(); }
    }
    private static async Task VerifyPermissionsAsync(FullNetApiFactory factory, bool baseOnly)
    {
        var identity = await factory.CreateHostIdentityAsync($"org-limited-{Guid.NewGuid():N}", [AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Create, baseOnly ? BaseRead : DocRead]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        var path = await CreateBaseAsync(client);
        using var created = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("独立机构权限", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var doc = (await created.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        await ExpectAsync(client, path + "/organizations", baseOnly ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await ExpectAsync(client, path + $"/documents/{doc.Id}/organizations", baseOnly ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        await SetAsync(client, path, [], 1, HttpStatusCode.Forbidden);
        await SetAsync(client, path + $"/documents/{doc.Id}", [], 1, HttpStatusCode.Forbidden);
    }
    private static async Task VerifyOpenApiAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        foreach (var suffix in new[] { "", "/documents/{documentId}" })
        foreach (var method in new[] { "get", "put" })
        {
            var op = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/ai/knowledge-bases/{knowledgeBaseId}" + suffix + "/organizations").GetProperty(method);
            Assert.AreEqual("ai" + (method == "get" ? "Get" : "Set") + "Knowledge" + (suffix.Length == 0 ? "" : "Document") + "Organizations", op.GetProperty("operationId").GetString());
            foreach (var status in new[] { "200", "400", "401", "403", "404", "409", "422" }) Assert.IsTrue(op.GetProperty("responses").TryGetProperty(status, out _));
        }
    }
    private static async Task<string> CreateBaseAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("机构授权", null));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return $"/api/v1/ai/knowledge-bases/{(await response.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!.Id}";
    }
    private static async Task<int> VersionAsync(HttpClient client, string path)
    { using var doc = JsonDocument.Parse(await client.GetStringAsync(path + "/organizations")); return doc.RootElement.GetProperty("version").GetInt32(); }
    private static async Task SetAsync(HttpClient client, string path, Guid[] ids, int version, HttpStatusCode expected)
    { using var response = await client.PutAsJsonAsync(path + "/organizations", new { organizationUnitIds = ids, version }); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync()); }
    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode expected, string? message = null)
    { using var response = await client.GetAsync(path); Assert.AreEqual(expected, response.StatusCode, message ?? await response.Content.ReadAsStringAsync()); }
    private static async Task<Guid> UnitAsync(FullNetApiFactory factory, TenantSummary tenant, bool active, Guid? parent = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(factory, tenant, "INSERT INTO fn_organization_unit (Id,TenantId,ParentId,Code,Name,DisplayOrder,IsActive,CreatedAtUtc,Version) VALUES (@Id,@TenantId,@ParentId,@Code,'机构授予',0,@IsActive,@CreatedAtUtc,1)",
            ("Id", id), ("ParentId", parent), ("Code", $"grant-{id:N}"), ("IsActive", active), ("CreatedAtUtc", DateTimeOffset.UtcNow));
        return id;
    }
    private static Task MembershipAsync(FullNetApiFactory factory, TenantSummary tenant, Guid unit, Guid user, bool active = true, bool insert = false) => ExecuteAsync(factory, tenant,
        insert ? "INSERT INTO fn_organization_user_unit (Id,TenantId,UserId,UnitId,IsPrimary,IsActive,CreatedAtUtc,Version) VALUES (@Id,@TenantId,@UserId,@UnitId,0,@IsActive,@CreatedAtUtc,1)"
            : "UPDATE fn_organization_user_unit SET IsActive=@IsActive WHERE TenantId=@TenantId AND UnitId=@UnitId AND UserId=@UserId",
        ("Id", Guid.CreateVersion7()), ("UnitId", unit), ("UserId", user), ("IsActive", active), ("CreatedAtUtc", DateTimeOffset.UtcNow));
    private static async Task ExecuteAsync(FullNetApiFactory factory, TenantSummary tenant, string sql, params (string Name, object? Value)[] values)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenant.Id, tenant.Identifier, tenant.Name));
        try { await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.ai.organization_grants_fixture", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), IdentitySqlParameters.Create(values)); }
        finally { current.Clear(); }
    }
    private static async Task RevokePermissionAsync(FullNetApiFactory factory, Guid user, string permission)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.ai.withdraw_organization_permission",
                "DELETE FROM fn_identity_role_permission WHERE RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId) AND PermissionCode=@Permission", SqlDataScope.HostOnly),
                IdentitySqlParameters.Create(("UserId", user), ("Permission", permission)));
        }
        finally { current.Clear(); }
    }
}
