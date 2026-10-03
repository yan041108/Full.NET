using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证角色授予与两层读取；旧令牌不能保持已移除的关系。</summary>
internal static class AiKnowledgeRoleGrantsApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeRoleGrantFaults faults)
    {
        await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Create, AiKnowledgePermissions.Read,
            AiKnowledgePermissions.MembersRead, AiKnowledgePermissions.MembersUpdate,
            AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.MembersRead, AiKnowledgeDocumentPermissions.MembersUpdate,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
            "tenancy.tenants.switch",
            "ai.knowledge_bases.roles_read", "ai.knowledge_bases.roles_update",
            "ai.knowledge_documents.roles_read", "ai.knowledge_documents.roles_update"];
        var owner = await factory.CreateHostIdentityAsync($"grant-owner-{Guid.NewGuid():N}", permissions);
        var member = await factory.CreateHostIdentityAsync($"grant-member-{Guid.NewGuid():N}", permissions);
        using var client = factory.CreateClientForHost("localhost");
        using var reader = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("角色两层授权", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        await ExpectAsync(client, path + "/roles", HttpStatusCode.OK);
        using var docCreated = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("受控文档", null));
        Assert.AreEqual(HttpStatusCode.Created, docCreated.StatusCode, await docCreated.Content.ReadAsStringAsync());
        var document = (await docCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var docPath = path + $"/documents/{document.Id}";
        var role = await InsertRoleAsync(factory, member.UserId);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await SetAsync(client, path, [role], 1, HttpStatusCode.OK);
        await ExpectAsync(reader, path, HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await SetAsync(client, docPath, [role], 1, HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await ExpectAsync(reader, path + "/roles", HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath + "/roles", HttpStatusCode.NotFound);
        using var page = JsonDocument.Parse(await reader.GetStringAsync(path + "/documents?pageSize=1"));
        Assert.AreEqual(1, page.RootElement.GetProperty("total").GetInt32());
        Assert.AreEqual(document.Id, page.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        // 旧用户名单接口只替换用户，空名单不能清除已经存在的角色授予。
        using var baseMembers = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], 2));
        Assert.AreEqual(HttpStatusCode.OK, baseMembers.StatusCode);
        using var docMembers = await client.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([], 2));
        Assert.AreEqual(HttpStatusCode.OK, docMembers.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await SetAsync(client, path, [], 2, HttpStatusCode.Conflict);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await MutateRoleAsync(factory, role, member.UserId, remove: true);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await MutateRoleAsync(factory, role, member.UserId, remove: false);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        await DisableRoleAsync(factory, role);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await SetAsync(client, path, [role], 3, HttpStatusCode.UnprocessableEntity);
        using var grants = JsonDocument.Parse(await client.GetStringAsync(path + "/roles"));
        Assert.AreEqual(3, grants.RootElement.GetProperty("version").GetInt32());
        Assert.AreEqual(role, grants.RootElement.GetProperty("roleIds")[0].GetGuid());
        // 合法用户的活动关系可以超过 SQL Server 参数上限，尾部角色不能被截断。
        Guid tail = default;
        for (var index = 0; index < 2105; index++) tail = await InsertRoleAsync(factory, member.UserId);
        await SetAsync(client, path, [tail], 3, HttpStatusCode.OK);
        await SetAsync(client, docPath, [tail], 3, HttpStatusCode.OK);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        using var ownCreated = await reader.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("大量关系所有者", null));
        Assert.AreEqual(HttpStatusCode.Created, ownCreated.StatusCode);
        var own = (await ownCreated.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        await ExpectAsync(reader, $"/api/v1/ai/knowledge-bases/{own.Id}", HttpStatusCode.OK);
        await VerifySourceRaceAsync(factory, faults, client, reader, owner.UserId, member.UserId, path);
        await VerifyTenantAsync(factory, client, reader, owner.AccessToken, member.AccessToken, owner.UserId, member.UserId, tail);
    }

    private static async Task VerifyTenantAsync(FullNetApiFactory factory, HttpClient hostOwner, HttpClient hostReader,
        string ownerToken, string memberToken, Guid ownerId, Guid memberId, Guid hostRole)
    {
        using var owner = factory.CreateClientForHost("acme.localhost");
        using var member = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(owner);
        foreach (var actor in new[] { ownerId, memberId })
            await ExecuteHostAsync(factory, new SqlStatement("test.ai.ensure_tenant_member",
                "INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) SELECT @Id,@TenantId,@UserId,'Member','Active',@CreatedAtUtc,@CreatedAtUtc,1 WHERE NOT EXISTS (SELECT 1 FROM fn_identity_tenant_member WHERE TenantId=@TenantId AND UserId=@UserId)", SqlDataScope.HostOnly),
                IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("TenantId", tenant.Id), ("UserId", actor), ("CreatedAtUtc", DateTimeOffset.UtcNow)));
        var tenantRole = await InsertRoleAsync(factory, memberId, tenant.Id);
        var foreignRole = await InsertRoleAsync(factory, memberId, Guid.CreateVersion7());
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(hostOwner, ownerToken, tenant.Id));
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(hostReader, memberToken, tenant.Id));
        using var created = await owner.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户角色目录", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        using var docCreated = await owner.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("租户角色文档", null));
        Assert.AreEqual(HttpStatusCode.Created, docCreated.StatusCode);
        var document = (await docCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var docPath = path + $"/documents/{document.Id}";
        await SetAsync(owner, path, [hostRole], 1, HttpStatusCode.UnprocessableEntity);
        await SetAsync(owner, path, [foreignRole], 1, HttpStatusCode.UnprocessableEntity);
        await SetAsync(owner, path, [tenantRole], 1, HttpStatusCode.OK);
        await ExpectAsync(member, path, HttpStatusCode.OK);
        await ExpectAsync(member, docPath, HttpStatusCode.NotFound);
        await SetAsync(owner, docPath, [tenantRole], 1, HttpStatusCode.OK);
        await ExpectAsync(member, docPath, HttpStatusCode.OK);
        await MutateRoleAsync(factory, tenantRole, memberId, true);
        await ExpectAsync(member, docPath, HttpStatusCode.NotFound);
        await MutateRoleAsync(factory, tenantRole, memberId, false);
        await DisableRoleAsync(factory, tenantRole);
        await ExpectAsync(member, docPath, HttpStatusCode.NotFound);
        await ExecuteHostAsync(factory, new SqlStatement("test.ai.enable_role", "UPDATE fn_identity_role SET IsActive=1 WHERE Id=@RoleId", SqlDataScope.HostOnly), IdentitySqlParameters.Create(("RoleId", tenantRole)));
        await ExpectAsync(member, docPath, HttpStatusCode.OK);
        // 即使关系残留，暂停租户成员也不得产生角色读取上下文。
        await ExecuteHostAsync(factory, new SqlStatement("test.ai.suspend_member", "UPDATE fn_identity_tenant_member SET Status='Suspended' WHERE UserId=@UserId AND TenantId=@TenantId", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("UserId", memberId), ("TenantId", tenant.Id)));
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
        try { Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<Full.NET.Modules.Identity.Contracts.IActiveRoleDirectory>().ListActiveRoleIdsForUserAsync(memberId)).Count); }
        finally { current.Clear(); }
    }

    private static async Task VerifySourceRaceAsync(FullNetApiFactory factory, AiKnowledgeRoleGrantFaults faults,
        HttpClient owner, HttpClient member, Guid ownerId, Guid memberId, string basePath)
    {
        using var created = await owner.PostAsJsonAsync(basePath + "/documents", new CreateAiKnowledgeDocumentRequest("来源复核", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var document = (await created.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var path = basePath + $"/documents/{document.Id}";
        var sourceRole = await InsertRoleAsync(factory, memberId);
        await SetAsync(owner, basePath, [sourceRole], 4, HttpStatusCode.OK);
        await SetAsync(owner, path, [sourceRole], 1, HttpStatusCode.OK);
        using var upload = await owner.PostAsync(path + $"/versions?version=2&requestKey={Guid.NewGuid()}&fileName=source.txt",
            new StringContent("打开后撤权不得泄露正文", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, upload.StatusCode, await upload.Content.ReadAsStringAsync());
        var operation = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        var source = path + $"/versions/{operation.VersionId}/source";
        await ExpectAsync(member, path + "/versions", HttpStatusCode.OK);
        await ExpectAsync(member, source, HttpStatusCode.OK);
        faults.AfterOpen = () => MutateRoleAsync(factory, sourceRole, memberId, true);
        await ExpectAsync(member, source, HttpStatusCode.NotFound);
        Assert.IsNotNull(faults.Opened);
        Assert.IsFalse(faults.Opened.CanRead, "撤权拒读必须释放已打开流。");
        await MutateRoleAsync(factory, sourceRole, memberId, false);
        var key = Guid.CreateVersion7();
        var secret = "fnk_" + Guid.NewGuid().ToString("N");
        await ExecuteHostAsync(factory, ApiKeySql.Insert, IdentitySqlParameters.Create(("Id", key), ("UserId", ownerId),
            ("DisplayName", "来源复核密钥"), ("KeyPrefix", secret[..16]), ("KeyHash", TokenHash.Compute(secret)),
            ("PermissionsJson", JsonSerializer.Serialize(new[] { AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead })),
            ("ExpiresAtUtc", DateTimeOffset.UtcNow.AddHours(1)), ("IsActive", true), ("LastUsedAtUtc", null), ("DisabledAtUtc", null),
            ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", null), ("Version", 1)));
        using var credential = factory.CreateClientForHost("localhost");
        credential.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", secret);
        await ExpectAsync(credential, source, HttpStatusCode.OK);
        faults.AfterOpen = () => ExecuteHostAsync(factory, new SqlStatement("test.ai.disable_key", "UPDATE fn_identity_api_key SET IsActive=0 WHERE Id=@Id", SqlDataScope.HostOnly), IdentitySqlParameters.Create(("Id", key)));
        await ExpectAsync(credential, source, HttpStatusCode.NotFound);
        Assert.IsFalse(faults.Opened!.CanRead, "凭据失效必须释放已打开流。");
        await ExecuteHostAsync(factory, new SqlStatement("test.ai.enable_key", "UPDATE fn_identity_api_key SET IsActive=1 WHERE Id=@Id", SqlDataScope.HostOnly), IdentitySqlParameters.Create(("Id", key)));
        using var signed = factory.CreateClientForHost("localhost");
        await ExpectSignedAsync(signed, source, secret, HttpStatusCode.Unauthorized);
        await ExpectAsync(credential, source, HttpStatusCode.OK);
        faults.AfterOpen = () => ExecuteHostAsync(factory, new SqlStatement("test.ai.withdraw_key_permission", "UPDATE fn_identity_api_key SET PermissionsJson=@PermissionsJson WHERE Id=@Id", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("Id", key), ("PermissionsJson", JsonSerializer.Serialize(new[] { AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read }))));
        await ExpectAsync(credential, source, HttpStatusCode.NotFound);
        Assert.IsFalse(faults.Opened!.CanRead, "凭据撤销精确权限必须释放流。");
        // 每层独立满足：文档角色不能越过知识库层，用户和角色可以组合匹配。
        async Task<int> Version(string resource) => (await owner.GetFromJsonAsync<AiKnowledgeRoleGrantsResponse>(resource + "/roles"))!.Version;
        await SetAsync(owner, basePath, [], await Version(basePath), HttpStatusCode.OK);
        await ExpectAsync(member, source, HttpStatusCode.NotFound);
        using (var grant = await owner.PutAsJsonAsync(basePath + "/members", new SetAiKnowledgeMembersRequest([memberId], await Version(basePath))))
            Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode);
        await ExpectAsync(member, source, HttpStatusCode.OK);
        using (var grant = await owner.PutAsJsonAsync(path + "/members", new SetAiKnowledgeDocumentMembersRequest([memberId], await Version(path))))
            Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode);
        await SetAsync(owner, path, [], await Version(path), HttpStatusCode.OK);
        await ExpectAsync(member, source, HttpStatusCode.OK);
        await SetAsync(owner, basePath, [sourceRole], await Version(basePath), HttpStatusCode.OK);
        using (var grant = await owner.PutAsJsonAsync(basePath + "/members", new SetAiKnowledgeMembersRequest([], await Version(basePath))))
            Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode);
        await ExpectAsync(member, source, HttpStatusCode.OK);
        var before = (await owner.GetFromJsonAsync<AiKnowledgeRoleGrantsResponse>(basePath + "/roles"))!;
        foreach (var invalid in new Guid[][] { [Guid.Empty], [sourceRole, sourceRole], Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray() })
            await SetAsync(owner, basePath, invalid, before.Version, HttpStatusCode.BadRequest);
        await SetAsync(owner, basePath, [Guid.CreateVersion7()], before.Version, HttpStatusCode.UnprocessableEntity);
        await SetAsync(owner, basePath, [], 0, HttpStatusCode.BadRequest);
        using (var injected = await owner.PutAsync(basePath + "/roles", new StringContent("{\"roleIds\":[],\"version\":1,\"tenantId\":null}", Encoding.UTF8, "application/json")))
            Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);
        var secondRole = await InsertRoleAsync(factory, memberId);
        faults.FailRoleInsertAt = 2;
        await SetAsync(owner, basePath, [sourceRole, secondRole], before.Version, HttpStatusCode.NotFound);
        var rolledBack = (await owner.GetFromJsonAsync<AiKnowledgeRoleGrantsResponse>(basePath + "/roles"))!;
        Assert.AreEqual(before.Version, rolledBack.Version, "失败必须回滚共享版本。");
        CollectionAssert.AreEqual(before.RoleIds.ToArray(), rolledBack.RoleIds.ToArray(), "失败必须恢复旧名单并撤销此前插入。");
        var writes = await Task.WhenAll(owner.PutAsJsonAsync(basePath + "/roles", new SetAiKnowledgeRoleGrantsRequest([sourceRole], before.Version)),
            owner.PutAsJsonAsync(basePath + "/roles", new SetAiKnowledgeRoleGrantsRequest([secondRole], before.Version)));
        try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, writes.Select(item => item.StatusCode).ToArray()); }
        finally { foreach (var write in writes) write.Dispose(); }
        faults.AfterOpen = () => ExecuteHostAsync(factory, new SqlStatement("test.ai.withdraw_source_permission",
            "DELETE FROM fn_identity_role_permission WHERE PermissionCode=@PermissionCode AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId)", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("UserId", ownerId), ("PermissionCode", AiKnowledgeDocumentPermissions.SourceRead)));
        await ExpectAsync(owner, source, HttpStatusCode.NotFound);
        Assert.IsFalse(faults.Opened!.CanRead, "交互权限撤销必须释放流。");
        await ExpectAsync(owner, source, HttpStatusCode.Forbidden);
    }

    private static async Task ExpectSignedAsync(HttpClient client, string path, string secret, HttpStatusCode status)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N")[..24];
        var prefix = secret[..16];
        var canonical = SignatureCanonicalRequest.BuildCanonicalString("GET", path, "", SignatureCanonicalRequest.ComputeContentHash([]), prefix, timestamp, nonce);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        // AI 未开放签名策略，维持既有拒绝边界，不能通过读取改造静默开放入口。
        request.Headers.Add(SignatureAuthenticationOptions.AccessKeyIdHeader, prefix);
        request.Headers.Add(SignatureAuthenticationOptions.TimestampHeader, timestamp);
        request.Headers.Add(SignatureAuthenticationOptions.NonceHeader, nonce);
        request.Headers.Add(SignatureAuthenticationOptions.SignatureHeader, SignatureCanonicalRequest.ComputeSignature(canonical, SignatureCanonicalRequest.ParseSigningKeyBytes(TokenHash.Compute(secret))));
        request.Headers.Add(SignatureAuthenticationOptions.SignatureVersionHeader, SignatureAuthenticationOptions.SupportedVersion);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task ExecuteHostAsync(FullNetApiFactory factory, SqlStatement statement, object parameters)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try { await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(statement, parameters); }
        finally { tenant.Clear(); }
    }

    private static async Task SetAsync(HttpClient client, string path, Guid[] roles, int version, HttpStatusCode expected)
    {
        using var response = await client.PutAsJsonAsync(path + "/roles", new { roleIds = roles, version });
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using var response = await client.GetAsync(path);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    private static async Task<Guid> InsertRoleAsync(FullNetApiFactory factory, Guid user, Guid? tenantId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try
        {
            var role = Guid.CreateVersion7();
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await command.ExecuteAsync(IdentitySql.InsertRole, IdentitySqlParameters.Create(("Id", role), ("TenantId", tenantId),
                ("ScopeKey", tenantId is { } id ? $"tenant:{id:N}" : "host"), ("Code", $"grant-{role:N}"), ("Name", "授权角色"), ("IsSystem", false),
                ("IsActive", true), ("IsSuperAdministrator", false), ("DataScopeKind", "all"),
                ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)));
            await command.ExecuteAsync(IdentitySql.EnsureUserRole, IdentitySqlParameters.Create(("UserId", user), ("RoleId", role)));
            return role;
        }
        finally { tenant.Clear(); }
    }
    private static async Task MutateRoleAsync(FullNetApiFactory factory, Guid role, Guid user, bool remove)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try
        {
            // 仅改变关系，保留权限角色与会话，才能证明旧令牌读取权威关系。
            var statement = remove ? new SqlStatement("test.ai.remove_role", "DELETE FROM fn_identity_user_role WHERE UserId=@UserId AND RoleId=@RoleId", SqlDataScope.HostOnly)
                : IdentitySql.EnsureUserRole;
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(statement,
                IdentitySqlParameters.Create(("UserId", user), ("RoleId", role)));
        }
        finally { tenant.Clear(); }
    }
    private static async Task DisableRoleAsync(FullNetApiFactory factory, Guid role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try { await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.ai.disable_role",
            "UPDATE fn_identity_role SET IsActive=0 WHERE Id=@RoleId", SqlDataScope.HostOnly), IdentitySqlParameters.Create(("RoleId", role))); }
        finally { tenant.Clear(); }
    }
}
