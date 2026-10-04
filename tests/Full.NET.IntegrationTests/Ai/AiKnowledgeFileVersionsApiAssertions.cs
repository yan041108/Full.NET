using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>通过真实 HTTP 与双库验证流上传、不变版本和来源读取。</summary>
internal static class AiKnowledgeFileVersionsApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        var owner = await factory.CreateHostIdentityAsync($"version-owner-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read,
             AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.VersionsRead,
             AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.ImportsRead, AiKnowledgeDocumentPermissions.ImportsRetry,
             AiKnowledgeDocumentPermissions.MembersUpdate, AiKnowledgePermissions.MembersUpdate, AiKnowledgePermissions.Update,
             AiKnowledgeDocumentPermissions.Delete, "tenancy.tenants.switch"]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var createBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("文件版本", null));
        Assert.AreEqual(HttpStatusCode.Created, createBase.StatusCode);
        var catalog = (await createBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var basePath = $"/api/v1/ai/knowledge-bases/{catalog.Id}/documents";
        using var create = await client.PostAsJsonAsync(basePath, new CreateAiKnowledgeDocumentRequest("受控上传", null));
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var document = (await create.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var path = basePath + $"/{document.Id}";
        var requestKey = Guid.NewGuid();
        var uploadPath = path + $"/versions?version=1&requestKey={requestKey}&fileName=source.txt";
        using var upload = await client.PostAsync(uploadPath,
            new StringContent("只有授权成员可读取正文", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, upload.StatusCode, await upload.Content.ReadAsStringAsync());
        using var result = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        Assert.AreEqual("completed", result.RootElement.GetProperty("state").GetString());
        var versionId = result.RootElement.GetProperty("versionId").GetGuid();
        using var source = await client.GetAsync(path + $"/versions/{versionId}/source");
        Assert.AreEqual(HttpStatusCode.OK, source.StatusCode, await source.Content.ReadAsStringAsync());
        Assert.AreEqual("只有授权成员可读取正文", await source.Content.ReadAsStringAsync());
        Assert.AreEqual("attachment", source.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual("application/octet-stream", source.Content.Headers.ContentType!.MediaType);
        Assert.IsTrue(source.Headers.CacheControl!.NoStore);
        Assert.AreEqual("nosniff", source.Headers.GetValues("X-Content-Type-Options").Single());
        var operationId = result.RootElement.GetProperty("id").GetGuid();
        using var replay = await client.PostAsync(uploadPath, new StringContent("只有授权成员可读取正文", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.AreEqual(operationId, (await replay.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.Id);
        using var mismatch = await client.PostAsync(uploadPath, new StringContent("另一个正文", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
        using var stale = await client.PostAsync(path + $"/versions?version=1&requestKey={Guid.NewGuid()}&fileName=source.txt", new StringContent("新正文", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        foreach (var invalid in new[] { ("evil.html", "text/plain", "hello"), ("../source.txt", "text/plain", "hello"), ("source.pdf", "application/pdf", "invalid"), ("source.txt", "text/plain", "") })
        {
            using var denied = await client.PostAsync(path + $"/versions?version=2&requestKey={Guid.NewGuid()}&fileName={Uri.EscapeDataString(invalid.Item1)}",
                new StringContent(invalid.Item3, Encoding.UTF8, invalid.Item2));
            Assert.AreEqual(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using var injected = await client.PostAsync(path + $"/versions?version=2&requestKey={Guid.NewGuid()}&fileName=source.txt&fileId={Guid.NewGuid()}", new StringContent("拒绝", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);
        using var second = await client.PostAsync(path + $"/versions?version=2&requestKey={Guid.NewGuid()}&fileName=source.md", new StringContent("# 第二个版本", Encoding.UTF8, "text/markdown"));
        Assert.AreEqual(HttpStatusCode.Accepted, second.StatusCode);
        var versions = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions?pageSize=1"))!;
        Assert.AreEqual(2L, versions.Total);
        Assert.AreEqual(2, versions.Items.Single().VersionNumber);
        Assert.AreEqual("pending_processing", versions.Items.Single().ProcessingStatus);
        using var oldSource = await client.GetAsync(path + $"/versions/{versionId}/source");
        Assert.AreEqual("只有授权成员可读取正文", await oldSource.Content.ReadAsStringAsync());
        Assert.AreEqual(3, (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(path))!.Version);
        var member = await factory.CreateHostIdentityAsync($"version-member-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
             AiKnowledgeDocumentPermissions.ImportsRead, AiKnowledgeDocumentPermissions.Upload]);
        using var reader = factory.CreateClientForHost("localhost");
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        async Task Expect(HttpClient requester, string target, HttpStatusCode status)
        { using var response = await requester.GetAsync(target); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync()); }
        var sourcePath = path + $"/versions/{versionId}/source";
        await AiKnowledgeSourcePersistenceAssertions.VerifyAsync(factory, client, catalog.Id, versionId,
            sourcePath, "只有授权成员可读取正文");
        await Expect(reader, sourcePath, HttpStatusCode.NotFound);
        using var grantBase = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{catalog.Id}/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, grantBase.StatusCode);
        await Expect(reader, sourcePath, HttpStatusCode.NotFound);
        using var grantDoc = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeDocumentMembersRequest([member.UserId], 3));
        Assert.AreEqual(HttpStatusCode.OK, grantDoc.StatusCode);
        await Expect(reader, sourcePath, HttpStatusCode.OK);
        await Expect(reader, path + $"/imports/{operationId}", HttpStatusCode.NotFound);
        using var nonOwner = await reader.PostAsync(path + $"/versions?version=4&requestKey={Guid.NewGuid()}&fileName=source.txt", new StringContent("无所有者权", Encoding.UTF8, "text/plain"));
        Assert.AreEqual(HttpStatusCode.NotFound, nonOwner.StatusCode);
        using var revokeDoc = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeDocumentMembersRequest([], 4));
        Assert.AreEqual(HttpStatusCode.OK, revokeDoc.StatusCode);
        await Expect(reader, sourcePath, HttpStatusCode.NotFound);
        using var disable = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{catalog.Id}", new UpdateAiKnowledgeBaseRequest("文件版本", null, false, 2));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        await Expect(client, sourcePath, HttpStatusCode.NotFound);
        using var enable = await client.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{catalog.Id}", new UpdateAiKnowledgeBaseRequest("文件版本", null, true, 3));
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        await Expect(client, sourcePath, HttpStatusCode.OK);

        // 实际租户上传经配额和资源所有权端口，Host 来源在同一所有者切换范围后仍拒读。
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, owner.AccessToken, tenant.Id));
        await Expect(tenantClient, sourcePath, HttpStatusCode.NotFound);
        using var tenantBaseCreated = await tenantClient.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户来源", null));
        var tenantBase = (await tenantBaseCreated.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var tenantDocuments = $"/api/v1/ai/knowledge-bases/{tenantBase.Id}/documents";
        using var tenantCreated = await tenantClient.PostAsJsonAsync(tenantDocuments, new CreateAiKnowledgeDocumentRequest("租户文件", null));
        var tenantDoc = (await tenantCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var tenantPath = tenantDocuments + $"/{tenantDoc.Id}";
        using var tenantUpload = await tenantClient.PostAsync(tenantPath + $"/versions?version=1&requestKey={Guid.NewGuid()}&fileName=tenant.pdf", new StringContent("%PDF-1.7\n样本", Encoding.UTF8, "application/pdf"));
        Assert.AreEqual(HttpStatusCode.Accepted, tenantUpload.StatusCode, await tenantUpload.Content.ReadAsStringAsync());
        var tenantOperation = (await tenantUpload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual("completed", tenantOperation.State);
        var tenantSourcePath = tenantPath + $"/versions/{tenantOperation.VersionId}/source";
        await Expect(tenantClient, tenantSourcePath, HttpStatusCode.OK);
        await AiKnowledgeSourcePersistenceAssertions.VerifyAsync(factory, tenantClient, tenantBase.Id,
            tenantOperation.VersionId, tenantSourcePath, "%PDF-1.7\n样本", tenant.Id);
        using var back = await tenantClient.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await back.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!.AccessToken);
        await Expect(client, tenantSourcePath, HttpStatusCode.NotFound);
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(5)) };
        using var deleted = await client.SendAsync(deletion);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        await Expect(client, sourcePath, HttpStatusCode.NotFound);
        var tombstoneOperation = (await client.GetFromJsonAsync<AiKnowledgeImportResponse>(path + $"/imports/{operationId}"))!;
        Assert.AreEqual("failed", tombstoneOperation.State);
    }
}
