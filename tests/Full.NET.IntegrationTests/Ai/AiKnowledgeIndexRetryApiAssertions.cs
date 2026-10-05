using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证恢复条件、原会话和已计费派生数据；模型及 Files 使用受控边界。</summary>
internal static class AiKnowledgeIndexRetryApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead,
            AiKnowledgeDocumentPermissions.IndexEnqueue, "ai.knowledge_documents.index_retry", "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"retry-{Guid.NewGuid():N}", permissions, password: FullNetApiFactory.TestPassword);
        var hostToken = owner.AccessToken;
        var outsider = await factory.CreateHostIdentityAsync($"retry-other-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost"); using var other = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost"); using var tenantOther = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        using (var doc = JsonDocument.Parse(await host.GetStringAsync("/openapi/v1.json")))
        {
            var operation = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/{taskId}/retry").GetProperty("post");
            Assert.AreEqual("aiRetryKnowledgeIndex", operation.GetProperty("operationId").GetString());
            Assert.IsFalse(operation.TryGetProperty("requestBody", out _)); Assert.IsTrue(operation.GetProperty("security").GetArrayLength() > 0);
            foreach (var status in new[] { "202", "400", "401", "403", "404", "409", "422" }) Assert.IsTrue(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        string? hostRetry = null, tenantRetry = null;
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, outsider.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant.Id));
                tenantOther.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(other, outsider.AccessToken, tenant.Id));
            }
            var client = scoped ? tenantClient : host; var deniedClient = scoped ? tenantOther : other;
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = owner.UserId, Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["ModelConfigId"] });
            row["OwnerUserId"] = owner.UserId; row["TenantId"] = scoped ? tenant.Id : null;
            var bytes = Encoding.UTF8.GetBytes("失败恢复与付费批次保留 recovery proof"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            var path = AiKnowledgeIndexStatusApiAssertions.Path(row);
            using (var enqueue = await client.PostAsync(path, null))
            {
                Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, await enqueue.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await enqueue.Content.ReadAsStringAsync()); row["TaskId"] = json.RootElement.GetProperty("taskId").GetGuid();
            }
            var taskId = (Guid)row["TaskId"]!; var retry = $"{path}/{taskId}/retry";
            if (!scoped) hostRetry = retry; else { tenantRetry = retry; await Expect(client, hostRetry!, HttpStatusCode.NotFound); }
            await Expect(deniedClient, retry, HttpStatusCode.NotFound);
            using var anonymous = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost"); await Expect(anonymous, retry, HttpStatusCode.Unauthorized);
            await Expect(client, retry + "?tenantId=" + tenant.Id, HttpStatusCode.BadRequest);
            await Expect(client, retry, HttpStatusCode.BadRequest, new StringContent("{}", Encoding.UTF8, "application/json"));
            await Expect(client, $"{path}/{Guid.CreateVersion7()}/retry", HttpStatusCode.Conflict);
            await Expect(client, retry, HttpStatusCode.Conflict);
            if (!scoped)
            {
                foreach (var permission in permissions.Where(x => x != "tenancy.tenants.switch"))
                {
                    var missing = await factory.CreateHostIdentityAsync($"retry-denied-{Guid.NewGuid():N}", permissions.Where(x => x != permission).ToArray());
                    using var missingClient = factory.CreateClientForHost("localhost"); missingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", missing.AccessToken);
                    await Expect(missingClient, retry, HttpStatusCode.Forbidden);
                }
            }
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner.UserId);
            var held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            await Expect(client, retry, HttpStatusCode.Conflict);
            Assert.AreEqual(held.LeaseToken, await db.QuerySingleAsync<Guid?>("SELECT LeaseToken FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", row));
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory { InputTokens = scoped ? null : 7 };
            var generation = AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, provider);
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            await Expect(client, retry, HttpStatusCode.Conflict);
            var saved = await RecoveryData(); var calls = provider.Calls;
            // 模拟派生数据已落盘而任务最终确认失败；恢复不能重新收费或重新派发。
            await Failed();
            var competing = await Task.WhenAll(client.PostAsync(retry, null), client.PostAsync(retry, null));
            using var first = competing[0]; using var second = competing[1];
            CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }, competing.Select(x => x.StatusCode).ToArray());
            await Accepted(competing.Single(x => x.StatusCode == HttpStatusCode.Accepted));
            await Expect(client, retry, HttpStatusCode.Conflict);
            Assert.AreEqual(saved, await RecoveryData());
            var oldLease = held.LeaseToken!.Value;
            held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            Assert.AreNotEqual(oldLease, held.LeaseToken!.Value);
            Assert.IsFalse(await tasks.FailAsync(binding, taskId, oldLease, AiKnowledgeIndexFailure.IndexUnavailable, default));
            Assert.IsFalse(await tasks.RenewAsync(binding, taskId, oldLease, default));
            Assert.AreEqual(held.LeaseToken, (await tasks.FindAsync(binding, taskId, default))!.LeaseToken);
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            Assert.AreEqual(calls, provider.Calls); Assert.AreEqual(saved, await RecoveryData());
            // 已付费载荷丢失时，重试可入队，但执行必须失败关闭且不能免费补发。
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row);
            var missingPayload = await RecoveryData(); await Failed(); await Accepted();
            held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            Assert.IsFalse(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            Assert.AreEqual(calls, provider.Calls); Assert.AreEqual(missingPayload, await RecoveryData());
            foreach (var attempt in new[] { 2, 3 })
            {
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',AttemptCount=@Attempt,LeaseExpiresAtUtc=@Expiry WHERE Id=@TaskId", new { TaskId = taskId, Attempt = attempt, Expiry = DateTime.UtcNow.AddMinutes(-1) });
                if (attempt == 2) await Expect(client, retry, HttpStatusCode.Conflict); else await Accepted();
            }
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='cancelled' WHERE Id=@TaskId", row); await Expect(client, retry, HttpStatusCode.Conflict);
            await Failed();
            foreach (var change in new[] { ("fn_ai_knowledge_document", "DocumentId", "IsDeleted=1", "IsDeleted=0", HttpStatusCode.NotFound),
                ("fn_ai_knowledge_base", "KnowledgeBaseId", "Version=2", "Version=1", HttpStatusCode.Conflict),
                ("fn_ai_model_config", "ModelConfigId", "IsEnabled=0", "IsEnabled=1", HttpStatusCode.NotFound),
                ("fn_ai_knowledge_version", "VersionId", "ContentType='application/pdf'", "ContentType='text/plain'", HttpStatusCode.UnprocessableEntity) })
            {
                await db.ExecuteAsync($"UPDATE {change.Item1} SET {change.Item3} WHERE Id=@Id", new { Id = row[change.Item2] });
                await Expect(client, retry, change.Item5);
                await db.ExecuteAsync($"UPDATE {change.Item1} SET {change.Item4} WHERE Id=@Id", new { Id = row[change.Item2] });
            }
            if (scoped)
            {
                await db.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE PermissionCode=@Permission AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@User)", new { Permission = "ai.knowledge_documents.index_retry", User = owner.UserId });
                await Expect(client, retry, HttpStatusCode.Forbidden);
            }
            else
            {
                using var fresh = factory.CreateClientForHost("localhost");
                hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(fresh, owner.Username);
                fresh.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
                await Expect(fresh, retry, HttpStatusCode.Conflict);
                Assert.AreEqual(missingPayload, await RecoveryData());
                // 独立任务先证明新会话可恢复，再移除其绑定；避免与新登录不匹配混为同一拒绝原因。
                var legacy = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, false);
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner WHERE Id=@Id", new { Owner = owner.UserId, Id = legacy["KnowledgeBaseId"] });
                var legacyPath = AiKnowledgeIndexStatusApiAssertions.Path(legacy);
                using var admitted = await fresh.PostAsync(legacyPath, null);
                Assert.AreEqual(HttpStatusCode.Accepted, admitted.StatusCode, await admitted.Content.ReadAsStringAsync());
                using var admittedJson = JsonDocument.Parse(await admitted.Content.ReadAsStringAsync());
                legacy["TaskId"] = admittedJson.RootElement.GetProperty("taskId").GetGuid();
                var legacyRetry = $"{legacyPath}/{legacy["TaskId"]}/retry";
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed' WHERE Id=@TaskId", legacy);
                using var healthy = await fresh.PostAsync(legacyRetry, null);
                Assert.AreEqual(HttpStatusCode.Accepted, healthy.StatusCode, await healthy.Content.ReadAsStringAsync());
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed' WHERE Id=@TaskId", legacy);
                await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@TaskId", legacy);
                await Expect(fresh, legacyRetry, HttpStatusCode.Conflict);
                // 新登录撤销旧会话；后续租户切换必须携带当前有效令牌。
                await Expect(client, retry, HttpStatusCode.Unauthorized);
                host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
            }

            Task Failed() => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed',AttemptCount=3,FailureCode='ai.knowledge.index_unavailable',LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@TaskId", row);
            async Task Accepted(HttpResponseMessage? existing = null)
            {
                using var response = existing ?? await client.PostAsync(retry, null); var text = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, text);
                using var json = JsonDocument.Parse(text); Assert.AreEqual(2, json.RootElement.EnumerateObject().Count());
                Assert.AreEqual(taskId, json.RootElement.GetProperty("taskId").GetGuid()); Assert.AreEqual(binding.VersionId, json.RootElement.GetProperty("versionId").GetGuid());
                var state = await tasks.FindAsync(binding, taskId, default); Assert.IsNotNull(state);
                Assert.AreEqual("queued", state.StateKey); Assert.AreEqual(0, state.AttemptCount); Assert.IsNull(state.LeaseToken); Assert.IsNull(state.LeaseExpiresAtUtc); Assert.IsNull(state.FailureCode);
            }
            async Task<string> RecoveryData()
            {
                var parts = new List<string>();
                foreach (var table in new[] { "fn_ai_knowledge_index_session", "fn_ai_knowledge_index_batch" })
                    parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE IndexTaskId=@TaskId ORDER BY Id", row)));
                parts.Add(JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", row)));
                foreach (var table in new[] { "fn_ai_knowledge_index_vector", "fn_ai_operation_budget" })
                    parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId) ORDER BY Id", row)));
                return string.Join("\n", parts);
            }
        }
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, owner.Username));
        // 撤销重试权限后先由精确权限拒绝，不能使用该结果冒充租户隔离证明。
        await Expect(host, tenantRetry!, HttpStatusCode.Forbidden);
    }

    private static async Task Expect(HttpClient client, string path, HttpStatusCode status, HttpContent? content = null)
    {
        using var response = await client.PostAsync(path, content); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
