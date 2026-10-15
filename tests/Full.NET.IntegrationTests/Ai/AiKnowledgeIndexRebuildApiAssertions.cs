using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证独立重建的原会话、幂等、费用隔离与活动代次切换；模型和文件使用受控边界。</summary>
internal static class AiKnowledgeIndexRebuildApiAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> { ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true" });
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Upload,
            AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeDocumentPermissions.IndexRead,
            AiKnowledgeDocumentPermissions.IndexRebuild, AiKnowledgeDocumentPermissions.IndexRetry, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"rebuild-{Guid.NewGuid():N}", permissions);
        var outsider = await factory.CreateHostIdentityAsync($"rebuild-other-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost"); using var otherHost = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        otherHost.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost"); using var otherTenant = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        using (var openapi = JsonDocument.Parse(await host.GetStringAsync("/openapi/v1.json")))
        {
            var operation = openapi.RootElement.GetProperty("paths").GetProperty("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/rebuild").GetProperty("post");
            Assert.AreEqual("aiRebuildKnowledgeIndex", operation.GetProperty("operationId").GetString());
            Assert.IsTrue(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
            var schema = openapi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AiKnowledgeIndexRebuildRequest");
            Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());
            CollectionAssert.AreEquivalent(new[] { "clientRequestId", "expectedTaskId", "expectedDocumentVersion", "expectedKnowledgeBaseVersion", "confirmAdditionalCost" }, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray());
        }
        string? hostPath = null;
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, outsider.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
                otherTenant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(otherHost, outsider.AccessToken, tenant.Id));
            }
            var client = scoped ? tenantClient : host; var other = scoped ? otherTenant : otherHost;
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = owner.UserId, Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["ModelConfigId"] });
            var bytes = Encoding.UTF8.GetBytes("显式重建独立收费，原活动代次保留至原子切换。"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            var path = AiKnowledgeIndexStatusApiAssertions.Path(row); var rebuild = path + "/rebuild";
            using (var enqueue = await client.PostAsync(path, null)) { Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, await enqueue.Content.ReadAsStringAsync()); row["TaskId"] = (await enqueue.Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>())!.TaskId; }
            var original = (Guid)row["TaskId"]!; var request = new AiKnowledgeIndexRebuildRequest(Guid.CreateVersion7(), original, 1, 1, true);
            await Expect(client, rebuild, request, HttpStatusCode.Conflict);
            await Expect(client, rebuild, request with { ConfirmAdditionalCost = false }, HttpStatusCode.BadRequest);
            await Expect(client, rebuild + "?tenantId=" + tenant.Id, request, HttpStatusCode.BadRequest);
            await Expect(other, rebuild, request, HttpStatusCode.NotFound);
            using (var anonymous = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost")) await Expect(anonymous, rebuild, request, HttpStatusCode.Unauthorized);
            using (var unknown = await client.PostAsync(rebuild, new StringContent(JsonSerializer.Serialize(request, JsonSerializerOptions.Web).TrimEnd('}') + ",\"tenantId\":null}", Encoding.UTF8, "application/json"))) Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
            if (!scoped)
            {
                hostPath = rebuild;
                foreach (var missing in permissions.Where(x => x is not "tenancy.tenants.switch" && x != AiKnowledgeDocumentPermissions.IndexRetry))
                {
                    var denied = await factory.CreateHostIdentityAsync($"rebuild-denied-{Guid.NewGuid():N}", permissions.Where(x => x != missing).ToArray());
                    using var deniedClient = factory.CreateClientForHost("localhost"); deniedClient.DefaultRequestHeaders.Authorization = new("Bearer", denied.AccessToken);
                    await Expect(deniedClient, rebuild, request, HttpStatusCode.Forbidden);
                }
            }
            else await Expect(client, hostPath!, request, HttpStatusCode.NotFound);
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>(); if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner.UserId);
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory { InputTokens = 7 };
            var generation = AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, model);
            var held = await tasks.AcquireAsync(binding, original, default); Assert.IsNotNull(held);
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, original, held.LeaseToken!.Value, default));
            var pointer = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row);
            var paid = await OldData(); var calls = model.Calls;
            // 未知用量或未结算结果不能通过新任务绕过，拒绝必须保持所有旧收费与载荷。
            await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET UsageStatus='unknown' WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row);
            await Expect(client, rebuild, request, HttpStatusCode.Conflict);
            await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET UsageStatus='known' WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row);
            Assert.AreEqual(paid, await OldData());
            await Expect(client, rebuild, request with { ExpectedDocumentVersion = 2 }, HttpStatusCode.Conflict);
            var responses = await Task.WhenAll(client.PostAsJsonAsync(rebuild, request), client.PostAsJsonAsync(rebuild, request));
            Guid next;
            try
            {
                foreach (var response in responses) Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
                next = (await responses[0].Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>())!.TaskId;
                Assert.AreEqual(next, (await responses[1].Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>())!.TaskId);
            }
            finally { foreach (var response in responses) response.Dispose(); }
            Assert.AreNotEqual(original, next); Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT Generation FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = next }));
            Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId", row));
            Assert.AreEqual(original, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row));
            Assert.AreEqual(paid, await OldData()); Assert.AreEqual(calls, model.Calls);
            var readParameters = new Dictionary<string, object?> { ["KnowledgeBaseId"] = binding.KnowledgeBaseId,
                ["DocumentId"] = binding.DocumentId, ["VersionId"] = binding.VersionId, ["OwnerUserId"] = owner.UserId,
                ["ActiveRoleIdsJson"] = "[]", ["ActiveOrganizationUnitIdsJson"] = "[]" };
            var active = await services.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForActive(provider, scoped), readParameters);
            Assert.IsNotNull(active); Assert.AreEqual(original, active.TaskId);
            Assert.IsTrue(await services.GetRequiredService<AiKnowledgeIndexActivationStore>().IsReadyForReadAsync(active, readParameters, default));
            Assert.IsNull(await tasks.AcquireAsync(binding, original, default));
            await Expect(client, rebuild, request with { ExpectedTaskId = next }, HttpStatusCode.Conflict);
            using (var oldRetry = await client.PostAsync($"{path}/{original}/retry", null)) Assert.AreEqual(HttpStatusCode.Conflict, oldRetry.StatusCode);
            var nextHeld = await tasks.AcquireAsync(binding, next, default); Assert.IsNotNull(nextHeld);
            foreach (var permission in new[] { AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRebuild })
            {
                var parameters = new { Permission = permission, Revoked = "ai.knowledge_documents.rebuild_revoked", User = owner.UserId };
                Assert.IsTrue(await db.ExecuteAsync("UPDATE fn_identity_role_permission SET PermissionCode=@Revoked WHERE PermissionCode=@Permission AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@User)", parameters) > 0);
                try
                {
                    Assert.IsFalse(await generation.ExecuteTextGenerationAsync(binding, next, nextHeld.LeaseToken!.Value, default));
                    Assert.AreEqual(calls, model.Calls); Assert.AreEqual(paid, await OldData());
                    Assert.AreEqual(original, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row));
                }
                finally { await db.ExecuteAsync("UPDATE fn_identity_role_permission SET PermissionCode=@Permission WHERE PermissionCode=@Revoked AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@User)", parameters); }
            }
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, next, nextHeld.LeaseToken!.Value, default));
            Assert.AreEqual(pointer, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row));
            Assert.AreEqual(next, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row));
            Assert.AreEqual(paid, await OldData()); Assert.IsTrue(model.Calls > calls);
            using var latest = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, latest.StatusCode);
            using var latestJson = JsonDocument.Parse(await latest.Content.ReadAsStringAsync()); Assert.AreEqual(next, latestJson.RootElement.GetProperty("taskId").GetGuid()); Assert.AreEqual("ready", latestJson.RootElement.GetProperty("state").GetString());
            async Task<string> OldData()
            {
                var parts = new List<string> { JsonSerializer.Serialize(await db.QuerySingleAsync("SELECT * FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", row)) };
                foreach (var table in new[] { "fn_ai_knowledge_index_session", "fn_ai_knowledge_index_batch" }) parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE IndexTaskId=@TaskId ORDER BY Id", row)));
                foreach (var table in new[] { "fn_ai_operation_budget", "fn_ai_knowledge_index_vector" }) parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId) ORDER BY Id", row)));
                parts.Add(JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", row))); return string.Join("\n", parts);
            }
        }
    }

    private static async Task Expect(HttpClient client, string path, AiKnowledgeIndexRebuildRequest request, HttpStatusCode expected)
    {
        using var response = await client.PostAsJsonAsync(path, request); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
