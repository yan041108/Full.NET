using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用数据库与身份夹具；单测不能证明真实策略、物化和两笔写入的原子性。</summary>
internal static class AiKnowledgeIndexEnqueueApiAssertions
{
    private const string Enqueue = "ai.knowledge_documents.index_enqueue";
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, Enqueue, "tenancy.tenants.switch"];
        var identity = await factory.CreateHostIdentityAsync($"enqueue-{Guid.NewGuid():N}", permissions, password: FullNetApiFactory.TestPassword);
        var outsider = await factory.CreateHostIdentityAsync($"enqueue-other-{Guid.NewGuid():N}", permissions);
        var denied = await factory.CreateHostIdentityAsync($"enqueue-denied-{Guid.NewGuid():N}", permissions.Where(x => x != Enqueue).ToArray());
        using var host = factory.CreateClientForHost("localhost");
        using var other = factory.CreateClientForHost("localhost");
        using var missingPermission = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        missingPermission.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", denied.AccessToken);
        await VerifyOpenApi(host);
        Dictionary<string, object?>? hostRow = null, tenantRow = null; Guid hostTask = default;
        using var ownerTenant = factory.CreateClientForHost("acme.localhost");
        using var otherTenant = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(ownerTenant);
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, identity.UserId);
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, outsider.UserId);
                ownerTenant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, identity.AccessToken, tenant.Id));
                otherTenant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(other, outsider.AccessToken, tenant.Id));
            }
            var client = scoped ? ownerTenant : host; var otherClient = scoped ? otherTenant : other;
            var row = await Seed(scoped); var path = Path(row);
            var task = await Accepted(client, row);
            await Expect(otherClient, path, HttpStatusCode.NotFound);
            if (!scoped) await Expect(missingPermission, path, HttpStatusCode.Forbidden);
            using var anonymous = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
            await Expect(anonymous, path, HttpStatusCode.Unauthorized);
            await Expect(client, path + "?ownerUserId=" + identity.UserId, HttpStatusCode.BadRequest);
            await Expect(client, path, HttpStatusCode.BadRequest, new StringContent("{\"tenantId\":null}", Encoding.UTF8, "application/json"));
            Assert.AreEqual(1L, await Count(row));
            Assert.AreEqual(task, await Accepted(client, row));
            var expectedSession = Guid.Parse(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(client.DefaultRequestHeaders.Authorization!.Parameter!, FullNetIdentityClaimTypes.SessionId)!);
            Assert.AreEqual(expectedSession, await db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = task }));
            Assert.AreEqual(1L, await Count(row));
            if (!scoped) { hostRow = row; hostTask = task; }
            else
            {
                // 切换租户会使原 Host 会话失效，不能用其 401 冒充有效主体的隔离证明。
                await Expect(host, path, HttpStatusCode.Unauthorized);
                await Expect(client, Path(hostRow!), HttpStatusCode.NotFound);
                tenantRow = row;
            }
            var markdown = await Seed(scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType='text/markdown' WHERE Id=@VersionId", markdown);
            await Accepted(client, markdown);
            foreach (var mutation in new[]
            {
                "UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId",
                "UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId",
                "UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId",
                "UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId",
                "INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@LeaseToken,@DocumentId,@FileId,2,'new.txt','text/plain','newhash',10,@OwnerUserId,@Now)",
            })
            {
                var invalid = await Seed(scoped); await db.ExecuteAsync(mutation, invalid);
                await Expect(client, Path(invalid), HttpStatusCode.NotFound); Assert.AreEqual(0L, await Count(invalid));
            }
            var pdf = await Seed(scoped); await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType='application/pdf' WHERE Id=@VersionId", pdf);
            await Expect(client, Path(pdf), HttpStatusCode.UnprocessableEntity); Assert.AreEqual(0L, await Count(pdf));
            var legacy = await Seed(scoped);
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc) VALUES (@TaskId,@DocumentId,@VersionId,1,1,@ModelConfigId,1,'queued',0,@Now,@Now)", legacy);
            await Expect(client, Path(legacy), HttpStatusCode.Conflict);
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@TaskId", legacy));
        }
        // 新登录有当前权限也不能替换已经冻结的原会话。
        host.DefaultRequestHeaders.Authorization = null;
        var newToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, identity.Username);
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        await Expect(host, Path(tenantRow!), HttpStatusCode.NotFound);
        await Expect(host, Path(hostRow!), HttpStatusCode.Conflict);
        Assert.AreEqual(hostTask, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = hostTask }));
        var fresh = await Seed(false); var freshTask = await Accepted(host, fresh);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var tasks = scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexTaskStore>();
        var sessions = scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexSessionService>();
        var binding = new AiKnowledgeIndexTaskBinding((Guid)fresh["KnowledgeBaseId"]!, (Guid)fresh["DocumentId"]!, (Guid)fresh["VersionId"]!, 1, 1, identity.UserId);
        var held = await tasks.AcquireAsync(binding, freshTask, default); Assert.IsNotNull(held);
        var original = await sessions.ReadHeldAsync(binding, freshTask, held.LeaseToken!.Value, default); Assert.IsNotNull(original);
        Assert.IsTrue(await sessions.RenewHeldAsync(binding, freshTask, held.LeaseToken.Value, original, default));
        await db.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE PermissionCode=@Permission AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@User)", new { Permission = Enqueue, User = identity.UserId });
        Assert.IsFalse(await sessions.RenewHeldAsync(binding, freshTask, held.LeaseToken.Value, original, default));
        var revoked = await Seed(false); await Expect(host, Path(revoked), HttpStatusCode.Forbidden); Assert.AreEqual(0L, await Count(revoked));

        async Task<Dictionary<string, object?>> Seed(bool scoped)
        {
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = identity.UserId, Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["ModelConfigId"] });
            row["OwnerUserId"] = identity.UserId; return row;
        }
        Task<long> Count(Dictionary<string, object?> row) => db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId", row);
        async Task<Guid> Accepted(HttpClient client, Dictionary<string, object?> row)
        {
            using var response = await client.PostAsync(Path(row), null); var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, body);
            using var json = JsonDocument.Parse(body); var task = json.RootElement.GetProperty("taskId").GetGuid();
            Assert.AreNotEqual(Guid.Empty, task); Assert.AreEqual(2, json.RootElement.EnumerateObject().Count());
            Assert.AreEqual((Guid)row["VersionId"]!, json.RootElement.GetProperty("versionId").GetGuid()); return task;
        }
    }

    private static async Task Expect(HttpClient client, string path, HttpStatusCode status, HttpContent? content = null)
    {
        using var response = await client.PostAsync(path, content);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task VerifyOpenApi(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index").GetProperty("post");
        Assert.AreEqual("aiEnqueueKnowledgeIndex", operation.GetProperty("operationId").GetString());
        Assert.IsFalse(operation.TryGetProperty("requestBody", out _)); Assert.IsTrue(operation.GetProperty("security").GetArrayLength() > 0);
        foreach (var status in new[] { "202", "400", "401", "403", "404", "409", "422" }) Assert.IsTrue(operation.GetProperty("responses").TryGetProperty(status, out _));
        var properties = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AiKnowledgeIndexEnqueueResponse").GetProperty("properties");
        CollectionAssert.AreEquivalent(new[] { "taskId", "versionId" }, properties.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var property in properties.EnumerateObject()) Assert.AreEqual("uuid", property.Value.GetProperty("format").GetString());
    }

    private static string Path(Dictionary<string, object?> row) => $"/api/v1/ai/knowledge-bases/{row["KnowledgeBaseId"]}/documents/{row["DocumentId"]}/versions/{row["VersionId"]}/index";
}
