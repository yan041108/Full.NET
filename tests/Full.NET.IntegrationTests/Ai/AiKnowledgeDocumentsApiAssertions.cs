using System.Net;
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

/// <summary>真实授权管线和双库验证标题保密、权威撤权、软删除、范围与版本竞争。</summary>
internal static class AiKnowledgeDocumentsApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update,
            AiKnowledgePermissions.MembersUpdate, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create,
            AiKnowledgeDocumentPermissions.Update, AiKnowledgeDocumentPermissions.Delete, AiKnowledgeDocumentPermissions.MembersRead,
            AiKnowledgeDocumentPermissions.MembersUpdate, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"doc-owner-{Guid.NewGuid():N}", permissions);
        var member = await factory.CreateHostIdentityAsync($"doc-member-{Guid.NewGuid():N}", permissions);
        var stranger = await factory.CreateHostIdentityAsync($"doc-stranger-{Guid.NewGuid():N}", permissions);
        var noDocRead = await factory.CreateHostIdentityAsync($"doc-no-read-{Guid.NewGuid():N}", [AiKnowledgePermissions.Read]);
        var noBaseRead = await factory.CreateHostIdentityAsync($"doc-no-base-{Guid.NewGuid():N}", [AiKnowledgeDocumentPermissions.Read]);
        using var client = factory.CreateClientForHost("localhost");
        using var reader = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var createBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("文档目录", null));
        Assert.AreEqual(HttpStatusCode.Created, createBase.StatusCode);
        var catalog = (await createBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var basePath = $"/api/v1/ai/knowledge-bases/{catalog.Id}";
        var path = basePath + "/documents";
        using var create = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("  授权文档  ", "  仅授权可读  "));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        var document = (await create.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        Assert.AreEqual("授权文档", document.Title);
        Assert.AreEqual("仅授权可读", document.Description);
        Assert.AreEqual("draft", document.Status);
        Assert.AreEqual(1, document.Version);
        Assert.AreEqual(catalog.Id, document.KnowledgeBaseId);
        var docPath = path + $"/{document.Id}";
        await ExpectAsync(client, path + $"/{Guid.Empty}", HttpStatusCode.NotFound);
        await ExpectAsync(client, $"/api/v1/ai/knowledge-bases/{Guid.Empty}/documents", HttpStatusCode.NotFound);
        using var emptyCreate = await client.PostAsJsonAsync($"/api/v1/ai/knowledge-bases/{Guid.Empty}/documents", new CreateAiKnowledgeDocumentRequest("空标识", null));
        await ProblemAsync(emptyCreate, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var secretCreate = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("未共享的机密标题", null));
        Assert.AreEqual(HttpStatusCode.Created, secretCreate.StatusCode);
        var secret = (await secretCreate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        foreach (var title in new string?[] { null, " ", new string('a', 201) })
        {
            using var invalid = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest(title!, null));
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.DocumentInvalid);
        }
        using var longDescription = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("文档", new string('b', 2001)));
        await ProblemAsync(longDescription, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.DocumentInvalid);
        // 不提供按他人 FileId 导入的接口，未知范围及状态字段均由严格 JSON 绑定拒绝。
        foreach (var field in new[] { "fileId", "tenantId", "ownerUserId", "status" })
        {
            using var injection = await client.PostAsync(path, new StringContent(
                "{\"title\":\"伪造\",\"description\":null,\"" + field + "\":\"ready\"}", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.BadRequest, injection.StatusCode);
        }
        Assert.AreEqual(2L, (await client.GetFromJsonAsync<PagedResult<AiKnowledgeDocumentResponse>>(path))!.Total);
        using var baseGrant = await client.PutAsJsonAsync(basePath + "/members",
            new SetAiKnowledgeMembersRequest([member.UserId, noDocRead.UserId, noBaseRead.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, baseGrant.StatusCode);
        // 仅知识库成员仍不能读标题，列表也不返回其数量。
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        Assert.AreEqual(0L, (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeDocumentResponse>>(path))!.Total);
        var initial = (await client.GetFromJsonAsync<AiKnowledgeDocumentMembersResponse>(docPath + "/members"))!;
        Assert.HasCount(0, initial.UserIds);
        foreach (var invalidIds in new IReadOnlyList<Guid>?[] { null, [Guid.Empty], [owner.UserId], [member.UserId, member.UserId],
                     Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray() })
        {
            using var invalid = await client.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest(invalidIds!, 1));
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.MembersInvalid);
        }
        using var grant = await client.PutAsJsonAsync(docPath + "/members",
            new SetAiKnowledgeDocumentMembersRequest([member.UserId, noDocRead.UserId, noBaseRead.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode, await grant.Content.ReadAsStringAsync());
        // 任一非知识库成员令整次替换回滚，包括已删除的旧名单和已插入的有效用户。
        using var mixed = await client.PutAsJsonAsync(docPath + "/members",
            new SetAiKnowledgeDocumentMembersRequest([member.UserId, stranger.UserId], 2));
        await ProblemAsync(mixed, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.DocumentMemberUnavailable);
        var preserved = (await client.GetFromJsonAsync<AiKnowledgeDocumentMembersResponse>(docPath + "/members"))!;
        Assert.AreEqual(2, preserved.Version);
        Assert.HasCount(3, preserved.UserIds);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        var allowed = (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeDocumentResponse>>(path + "?page=1&pageSize=1"))!;
        Assert.AreEqual(1L, allowed.Total);
        Assert.HasCount(1, allowed.Items);
        Assert.AreEqual(document.Id, allowed.Items[0].Id);
        await ExpectAsync(reader, path + $"/{secret.Id}", HttpStatusCode.NotFound);
        await ExpectAsync(reader, docPath + "/members", HttpStatusCode.NotFound);
        using var memberCreate = await reader.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("越权", null));
        await ProblemAsync(memberCreate, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var memberEdit = await reader.PutAsJsonAsync(docPath, new UpdateAiKnowledgeDocumentRequest("越权", null, 2));
        await ProblemAsync(memberEdit, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var memberGrant = await reader.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([], 2));
        await ProblemAsync(memberGrant, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var memberDelete = await DeleteAsync(reader, docPath, 2);
        await ProblemAsync(memberDelete, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        foreach (var token in new[] { noDocRead.AccessToken, noBaseRead.AccessToken })
        {
            reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await ExpectAsync(reader, docPath, HttpStatusCode.Forbidden);
            await ExpectAsync(reader, path, HttpStatusCode.Forbidden);
        }
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stranger.AccessToken);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100" })
        {
            using var invalidPage = await client.GetAsync(path + query);
            await ProblemAsync(invalidPage, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.PageInvalid);
        }
        using var stale = await client.PutAsJsonAsync(docPath, new UpdateAiKnowledgeDocumentRequest("旧版本", null, 1));
        await ProblemAsync(stale, HttpStatusCode.Conflict, AiKnowledgeErrorCodes.DocumentVersionConflict);
        using var zeroVersion = await client.PutAsJsonAsync(docPath, new UpdateAiKnowledgeDocumentRequest("零版本", null, 0));
        await ProblemAsync(zeroVersion, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.DocumentInvalid);
        var writes = await Task.WhenAll(client.PutAsJsonAsync(docPath, new UpdateAiKnowledgeDocumentRequest("并发一", null, 2)),
            client.PutAsJsonAsync(docPath, new UpdateAiKnowledgeDocumentRequest("并发二", null, 2)));
        try
        {
            Assert.AreEqual(1, writes.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, writes.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var winner = (await writes.Single(response => response.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            Assert.AreEqual(winner.Title, (await reader.GetFromJsonAsync<AiKnowledgeDocumentResponse>(docPath))!.Title);
            Assert.AreEqual(3, winner.Version);
        }
        finally { foreach (var response in writes) response.Dispose(); }
        using var revokeDocument = await client.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([], 3));
        Assert.AreEqual(HttpStatusCode.OK, revokeDocument.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        Assert.AreEqual(0L, (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeDocumentResponse>>(path))!.Total);
        using var regrantDocument = await client.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([member.UserId], 4));
        Assert.AreEqual(HttpStatusCode.OK, regrantDocument.StatusCode);
        using var revokeBase = await client.PutAsJsonAsync(basePath + "/members", new SetAiKnowledgeMembersRequest([], 2));
        Assert.AreEqual(HttpStatusCode.OK, revokeBase.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        using var regrantBase = await client.PutAsJsonAsync(basePath + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 3));
        Assert.AreEqual(HttpStatusCode.OK, regrantBase.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);
        using var disable = await client.PutAsJsonAsync(basePath, new UpdateAiKnowledgeBaseRequest("文档目录", null, false, 4));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(client, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound);
        using var disabledCreate = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("禁用目录", null));
        await ProblemAsync(disabledCreate, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using var enable = await client.PutAsJsonAsync(basePath, new UpdateAiKnowledgeBaseRequest("文档目录", null, true, 5));
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.OK);

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var tenantReader = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, owner.AccessToken, tenant.Id));
        await ExpectAsync(tenantClient, docPath, HttpStatusCode.NotFound);
        using var tenantCreateBase = await tenantClient.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户文档目录", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantCreateBase.StatusCode);
        var tenantBase = (await tenantCreateBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var tenantPath = $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/documents";
        using var tenantCreate = await tenantClient.PostAsJsonAsync(tenantPath, new CreateAiKnowledgeDocumentRequest("租户文档", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantCreate.StatusCode);
        var tenantDoc = (await tenantCreate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var tenantDocPath = tenantPath + $"/{tenantDoc.Id}";
        using var wrongRangeGrant = await tenantClient.PutAsJsonAsync(tenantDocPath + "/members", new SetAiKnowledgeDocumentMembersRequest([member.UserId], 1));
        await ProblemAsync(wrongRangeGrant, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.DocumentMemberUnavailable);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, member.UserId);
        using var tenantBaseGrant = await tenantClient.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{tenantBase.Id}/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, tenantBaseGrant.StatusCode);
        using var tenantDocGrant = await tenantClient.PutAsJsonAsync(tenantDocPath + "/members", new SetAiKnowledgeDocumentMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, tenantDocGrant.StatusCode);
        tenantReader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(reader, member.AccessToken, tenant.Id));
        await ExpectAsync(tenantReader, tenantDocPath, HttpStatusCode.OK);
        await ExpectAsync(tenantReader, docPath, HttpStatusCode.NotFound);
        using var tenantRevoke = await tenantClient.PutAsJsonAsync(tenantDocPath + "/members", new SetAiKnowledgeDocumentMembersRequest([], 2));
        Assert.AreEqual(HttpStatusCode.OK, tenantRevoke.StatusCode);
        await ExpectAsync(tenantReader, tenantDocPath, HttpStatusCode.NotFound);
        // 切范围使旧令牌失效；先取得新的 Host 会话令牌，再验证同一所有者的跨租户拒绝。
        using var ownerBack = await tenantClient.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        Assert.AreEqual(HttpStatusCode.OK, ownerBack.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ownerBack.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!.AccessToken);
        using var memberBack = await tenantReader.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        Assert.AreEqual(HttpStatusCode.OK, memberBack.StatusCode);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await memberBack.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!.AccessToken);
        await ExpectAsync(client, tenantDocPath, HttpStatusCode.NotFound);
        using var staleDelete = await DeleteAsync(client, docPath, 4);
        await ProblemAsync(staleDelete, HttpStatusCode.Conflict, AiKnowledgeErrorCodes.DocumentVersionConflict);
        using var delete = await DeleteAsync(client, docPath, 5);
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        await ExpectAsync(reader, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(client, docPath, HttpStatusCode.NotFound);
        await ExpectAsync(client, docPath + "/members", HttpStatusCode.NotFound);
        using var deletedGrant = await client.PutAsJsonAsync(docPath + "/members", new SetAiKnowledgeDocumentMembersRequest([member.UserId], 6));
        await ProblemAsync(deletedGrant, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        Assert.AreEqual(1L, (await client.GetFromJsonAsync<PagedResult<AiKnowledgeDocumentResponse>>(path))!.Total);
        using var repeatDelete = await DeleteAsync(client, docPath, 5);
        await ProblemAsync(repeatDelete, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        var limited = await factory.CreateHostIdentityAsync($"doc-limited-{Guid.NewGuid():N}", [AiKnowledgePermissions.Create]);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limited.AccessToken);
        using var limitedBaseCreate = await reader.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("限权所有者", null));
        var limitedBase = (await limitedBaseCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        using var deniedCreate = await reader.PostAsJsonAsync($"/api/v1/ai/knowledge-bases/{limitedBase.Id}/documents", new CreateAiKnowledgeDocumentRequest("缺少独立创建权", null));
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(version)) };
        return await client.SendAsync(request);
    }
    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(path);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        if (status == HttpStatusCode.NotFound) await ProblemAsync(response, status, AiKnowledgeErrorCodes.DocumentNotFound);
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }
}
