using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 HTTP 与双库权威查询；仅有入队成功不能证明索引就绪。</summary>
internal static class AiKnowledgeIndexStatusApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.IndexRead];
        var owner = await factory.CreateHostIdentityAsync($"index-state-{Guid.NewGuid():N}", [.. permissions,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexEnqueue, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        var member = await factory.CreateHostIdentityAsync($"index-reader-{Guid.NewGuid():N}", [.. permissions, "tenancy.tenants.switch"]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var reader = factory.CreateClientForHost("localhost");
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost"); using var tenantReader = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        using (var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")))
        {
            var operation = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index").GetProperty("get");
            Assert.AreEqual("aiGetKnowledgeIndexStatus", operation.GetProperty("operationId").GetString());
            Assert.IsFalse(operation.TryGetProperty("requestBody", out _));
            foreach (var status in new[] { "200", "400", "401", "403", "404", "409" }) Assert.IsTrue(operation.GetProperty("responses").TryGetProperty(status, out _));
            var properties = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AiKnowledgeIndexStatusResponse").GetProperty("properties");
            CollectionAssert.AreEquivalent(new[] { "versionId", "taskId", "state", "failureCode" }, properties.EnumerateObject().Select(x => x.Name).ToArray());
        }
        Dictionary<string, object?>? hostRow = null, tenantRow = null;
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, member.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, owner.AccessToken, tenant.Id));
                tenantReader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(reader, member.AccessToken, tenant.Id));
            }
            var ownerClient = scoped ? tenantClient : client; var memberClient = scoped ? tenantReader : reader;
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = owner.UserId, Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["ModelConfigId"] });
            row["OwnerUserId"] = owner.UserId; row["TenantId"] = scoped ? tenant.Id : null;
            var path = Path(row); await State(ownerClient, path, "pending_processing");
            await Expect(memberClient, path, HttpStatusCode.NotFound);
            using var anonymous = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost"); await Expect(anonymous, path, HttpStatusCode.Unauthorized);
            await Expect(ownerClient, path + "?tenantId=" + tenant.Id, HttpStatusCode.BadRequest);
            using (var request = new HttpRequestMessage(HttpMethod.Get, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") })
            using (var response = await ownerClient.SendAsync(request)) Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            if (!scoped)
            {
                hostRow = row;
                foreach (var permission in permissions)
                {
                    var denied = await factory.CreateHostIdentityAsync($"index-denied-{Guid.NewGuid():N}", permissions.Where(x => x != permission).ToArray());
                    using var deniedClient = factory.CreateClientForHost("localhost"); deniedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", denied.AccessToken);
                    await Expect(deniedClient, path, HttpStatusCode.Forbidden);
                }
            }
            else { tenantRow = row; await Expect(ownerClient, Path(hostRow!), HttpStatusCode.NotFound); }
            var bytes = Encoding.UTF8.GetBytes("索引状态实际完整性 source proof"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            using (var admitted = await ownerClient.PostAsync(path, null))
            {
                Assert.AreEqual(HttpStatusCode.Accepted, admitted.StatusCode, await admitted.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await admitted.Content.ReadAsStringAsync()); row["TaskId"] = json.RootElement.GetProperty("taskId").GetGuid();
            }
            await State(ownerClient, path, "queued");
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner.UserId);
            var taskId = (Guid)row["TaskId"]!; var held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            await State(ownerClient, path, "processing");
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory();
            Assert.IsTrue(await Generation(services, tasks, files, provider).ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            await State(ownerClient, path, "ready"); var calls = provider.Calls; var opens = files.Opens;
            var payload = await db.QuerySingleAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@TaskId", row);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=(SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", new { TaskId = taskId, Payload = new byte[8] });
            await State(ownerClient, path, "failed");
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=(SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", new { TaskId = taskId, Payload = payload });
            await State(ownerClient, path, "ready");
            foreach (var pair in new[] { ("fn_ai_model_config", "ModelConfigId", "IsEnabled=0", "IsEnabled=1"),
                ("fn_ai_knowledge_version", "VersionId", "SizeBytes=SizeBytes+1", "SizeBytes=SizeBytes-1"),
                ("fn_ai_knowledge_document", "DocumentId", "Version=2", "Version=1") })
            {
                await db.ExecuteAsync($"UPDATE {pair.Item1} SET {pair.Item3} WHERE Id=@Id", new { Id = row[pair.Item2] }); await State(ownerClient, path, "failed");
                await db.ExecuteAsync($"UPDATE {pair.Item1} SET {pair.Item4} WHERE Id=@Id", new { Id = row[pair.Item2] }); await State(ownerClient, path, "ready");
            }
            row["ReaderId"] = member.UserId; row["GrantId"] = Guid.CreateVersion7();
            // 最新来源及同版本的新任务都会使旧代次失去 ready，不能只看旧活动指针。
            var newer = Guid.CreateVersion7();
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@Id,@DocumentId,@FileId,2,'new.txt','text/plain',@Hash,@Size,@User,@Now)",
                new { Id = newer, DocumentId = binding.DocumentId, FileId = (Guid)row["FileId"]!, Hash = hash, Size = bytes.Length, User = owner.UserId, Now = DateTime.UtcNow });
            await State(ownerClient, path, "failed"); await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_version WHERE Id=@Id", new { Id = newer });
            await State(ownerClient, path, "ready");
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,@DocumentId,@VersionId,2,1,@ModelConfigId,1,'queued',0,@Now,@Now)",
                new { Id = newer, DocumentId = binding.DocumentId, VersionId = binding.VersionId, ModelConfigId = (Guid)row["ModelConfigId"]!, Now = DateTime.UtcNow.AddMinutes(1) });
            await State(ownerClient, path, "failed"); await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = newer });
            await State(ownerClient, path, "ready");
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@GrantId,@KnowledgeBaseId,@ReaderId,@Now)", row);
            await Expect(memberClient, path, HttpStatusCode.NotFound); row["GrantId"] = Guid.CreateVersion7();
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@GrantId,@DocumentId,@ReaderId,@Now)", row);
            await State(memberClient, path, "ready");
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE DocumentId=@DocumentId", row); await Expect(memberClient, path, HttpStatusCode.NotFound);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_member WHERE KnowledgeBaseId=@KnowledgeBaseId", row);
            if (!scoped)
            {
                row["RoleId"] = await db.QuerySingleAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@Id", new { Id = member.UserId });
                row["GrantId"] = Guid.CreateVersion7(); await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_role (Id,KnowledgeBaseId,RoleId,CreatedAtUtc) VALUES (@GrantId,@KnowledgeBaseId,@RoleId,@Now)", row);
                await Expect(memberClient, path, HttpStatusCode.NotFound); row["GrantId"] = Guid.CreateVersion7();
                await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_role (Id,DocumentId,RoleId,CreatedAtUtc) VALUES (@GrantId,@DocumentId,@RoleId,@Now)", row);
                await State(memberClient, path, "ready");
                await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_role WHERE DocumentId=@DocumentId", row); await Expect(memberClient, path, HttpStatusCode.NotFound);
            }
            else
            {
                row["UnitId"] = Guid.CreateVersion7(); row["MembershipId"] = Guid.CreateVersion7(); row["Code"] = Guid.NewGuid().ToString("N");
                await db.ExecuteAsync("INSERT INTO fn_organization_unit (Id,TenantId,Code,Name,DisplayOrder,IsActive,CreatedAtUtc,Version) VALUES (@UnitId,@TenantId,@Code,'状态读取机构',0,1,@Now,1)", row);
                await db.ExecuteAsync("INSERT INTO fn_organization_user_unit (Id,TenantId,UserId,UnitId,IsPrimary,IsActive,CreatedAtUtc,Version) VALUES (@MembershipId,@TenantId,@ReaderId,@UnitId,0,1,@Now,1)", row);
                row["GrantId"] = Guid.CreateVersion7(); await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_org (Id,KnowledgeBaseId,OrganizationUnitId,CreatedAtUtc) VALUES (@GrantId,@KnowledgeBaseId,@UnitId,@Now)", row);
                await Expect(memberClient, path, HttpStatusCode.NotFound); row["GrantId"] = Guid.CreateVersion7();
                await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_org (Id,DocumentId,OrganizationUnitId,CreatedAtUtc) VALUES (@GrantId,@DocumentId,@UnitId,@Now)", row);
                await State(memberClient, path, "ready");
                await db.ExecuteAsync("UPDATE fn_organization_user_unit SET IsActive=0 WHERE Id=@MembershipId", row); await Expect(memberClient, path, HttpStatusCode.NotFound);
                await db.ExecuteAsync("UPDATE fn_organization_user_unit SET IsActive=1 WHERE Id=@MembershipId", row); await State(memberClient, path, "ready");
            }
            // 原写入权限撤销后，当前读者依然可复核持久完成证明；写入重试保持原会话授权。
            await db.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE PermissionCode=@Permission AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@User)", new { Permission = AiKnowledgeDocumentPermissions.IndexEnqueue, User = owner.UserId });
            await State(ownerClient, path, "ready");
            Assert.IsFalse(await services.GetRequiredService<AiKnowledgeIndexActivationStore>().IsActiveAsync(binding, taskId, held.LeaseToken.Value, default));
            // 下一轮需要独立有效写入会话；恢复被测试权限只用于此隔离夹具。
            await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT RoleId,@Permission FROM fn_identity_user_role WHERE UserId=@User", new { Permission = AiKnowledgeDocumentPermissions.IndexEnqueue, User = owner.UserId });
            Assert.AreEqual(calls, provider.Calls); Assert.AreEqual(opens, files.Opens);
            if (scoped)
            {
                // 真正撤销原 Refresh 会话，另一名只有读取权限的机构成员仍可读完整索引。
                var original = await db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@TaskId", row);
                Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now WHERE Id=@Id", new { Now = DateTime.UtcNow, Id = original }));
                await Expect(ownerClient, path, HttpStatusCode.Unauthorized); await State(memberClient, path, "ready");
                Assert.IsFalse(await services.GetRequiredService<AiKnowledgeIndexActivationStore>().IsActiveAsync(binding, taskId, held.LeaseToken.Value, default));
                client.DefaultRequestHeaders.Authorization = null;
                var fresh = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, owner.Username);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
                ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, fresh, tenant.Id));
                await State(ownerClient, path, "ready");
            }
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId", row); await State(ownerClient, path, "deleting");
            await Expect(memberClient, path, HttpStatusCode.NotFound);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row);
            await State(ownerClient, path, "deleting");
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", row);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE DocumentId=@DocumentId", row); await State(ownerClient, path, "deleting");
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='failed' WHERE DocumentId=@DocumentId", row); await State(ownerClient, path, "deleted");
        }
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, owner.Username));
        await Expect(client, Path(tenantRow!), HttpStatusCode.NotFound);
    }

    internal static AiKnowledgeIndexPersistenceService Generation(IServiceProvider services, AiKnowledgeIndexTaskStore tasks,
        AiKnowledgeIndexEmbeddingAssertions.ControlledFiles files, AiKnowledgeIndexEmbeddingAssertions.ControlledFactory provider)
    {
        var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
        var transaction = services.GetRequiredService<ICommandTransaction>(); var state = services.GetRequiredService<IDataTransactionState>();
        var tenant = services.GetRequiredService<ICurrentTenant>(); var clock = services.GetRequiredService<IClock>(); var ids = services.GetRequiredService<IIdGenerator>();
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>(); var authorization = services.GetRequiredService<IBackgroundSessionAuthorization>();
        var sessions = services.GetRequiredService<AiKnowledgeIndexSessionService>(); var budget = services.GetRequiredService<IAiOperationBudgetStore>();
        var admission = new AiKnowledgeIndexBudgetService(query, tasks, new(query, command, tasks, ids, options), budget);
        var execution = new AiKnowledgeIndexEmbeddingService(query, tenant, state, tasks, sessions, admission, budget, authorization,
            files, files, files, [provider], services.GetRequiredService<AiModelBindingScope>());
        return new(execution, new(query, command, transaction, state, tenant, clock, tasks, sessions, authorization, options),
            new(query, command, transaction, state, tenant, clock, tasks, sessions, authorization, options), services.GetRequiredService<AiKnowledgeIndexActivationStore>(), sessions);
    }

    private static async Task State(HttpClient client, string path, string state)
    {
        using var response = await client.GetAsync(path); var body = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        using var json = JsonDocument.Parse(body); Assert.AreEqual(state, json.RootElement.GetProperty("state").GetString());
        CollectionAssert.AreEquivalent(new[] { "versionId", "taskId", "state", "failureCode" }, json.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.AreEqual(state == "failed" ? "ai.knowledge.index_unavailable" : null, json.RootElement.GetProperty("failureCode").GetString());
    }

    private static async Task Expect(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(path); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string Path(Dictionary<string, object?> row) => $"/api/v1/ai/knowledge-bases/{row["KnowledgeBaseId"]}/documents/{row["DocumentId"]}/versions/{row["VersionId"]}/index";
}
