using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产索引链路生成真实派生行，再通过 HTTP 删除验证清理和审计保留。</summary>
internal static class AiKnowledgeIndexCleanupApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiKnowledgeIndexCleanupFaults faults)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead,
            AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeDocumentPermissions.IndexRead,
            AiKnowledgeDocumentPermissions.Delete, AiKnowledgeDocumentPermissions.ImportsRetry, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"cleanup-{Guid.NewGuid():N}", permissions, password: FullNetApiFactory.TestPassword);
        var outsider = await factory.CreateHostIdentityAsync($"cleanup-other-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost");
        using var other = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var tenantOther = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        string? hostCleanup = null, tenantCleanup = null;
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, outsider.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
                tenantOther.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(other, outsider.AccessToken, tenant.Id));
            }
            var client = scoped ? tenantClient : host;
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = owner.UserId, Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = scoped ? tenant.Id : (Guid?)null, Id = row["ModelConfigId"] });
            row["OwnerUserId"] = owner.UserId; row["TenantId"] = scoped ? tenant.Id : null;
            var bytes = Encoding.UTF8.GetBytes("删除索引并保留计费凭据 cleanup proof"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            var path = AiKnowledgeIndexStatusApiAssertions.Path(row);
            using (var enqueue = await client.PostAsync(path, null))
            {
                Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, await enqueue.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await enqueue.Content.ReadAsStringAsync()); row["TaskId"] = json.RootElement.GetProperty("taskId").GetGuid();
            }
            var taskId = (Guid)row["TaskId"]!;
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner.UserId);
            var held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory { InputTokens = scoped ? null : 7 };
            var generation = AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, provider);
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            var audit = await Audit();
            var document = $"/api/v1/ai/knowledge-bases/{binding.KnowledgeBaseId}/documents/{binding.DocumentId}";
            var cleanup = document + "/imports/cleanup";
            if (!scoped) hostCleanup = cleanup; else { tenantCleanup = cleanup; await Expect(client, hostCleanup!, HttpStatusCode.NotFound); }
            if (scoped)
            {
                using var crossScope = await AiKnowledgeDeleteReplayAssertions.Delete(client, hostCleanup!.Replace("/imports/cleanup", "", StringComparison.Ordinal), 1);
                Assert.AreEqual(HttpStatusCode.NotFound, crossScope.StatusCode);
            }
            await Expect(scoped ? tenantOther : other, cleanup, HttpStatusCode.NotFound);
            using var anonymous = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
            await Expect(anonymous, cleanup, HttpStatusCode.Unauthorized);
            if (!scoped)
            {
                var missing = await factory.CreateHostIdentityAsync($"cleanup-denied-{Guid.NewGuid():N}", permissions.Where(x => x != AiKnowledgeDocumentPermissions.ImportsRetry).ToArray());
                using var denied = factory.CreateClientForHost("localhost"); denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", missing.AccessToken);
                await Expect(denied, cleanup, HttpStatusCode.Forbidden);
            }
            await Expect(client, cleanup, HttpStatusCode.OK);
            Assert.AreEqual(3, await Derived(), "存活文档不可清理");
            faults.VersionId = binding.VersionId; faults.TenantId = scoped ? tenant.Id : null;
            faults.InterruptCleanup = true; var interrupted = faults.Interrupted; var releases = faults.Releases;
            using var deletion = new HttpRequestMessage(HttpMethod.Delete, document) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(1)) };
            using var deleted = await client.SendAsync(deletion);
            Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
            Assert.AreEqual(interrupted + 1, faults.Interrupted);
            Assert.AreEqual(3, await Derived(), "事务中断必须回滚当前任务的所有派生删除");
            Assert.AreEqual(releases, faults.Releases, "AI 事务失败不得进入 Files");
            await State("deleting");
            await Replay();
            Assert.AreEqual(3, await Derived(), "重放可以确认墓碑，但不能把清理故障报告为已完成");
            Assert.AreEqual(audit, await Audit());
            await Expect(client, cleanup, HttpStatusCode.InternalServerError);
            Assert.AreEqual(3, await Derived());
            faults.InterruptCleanup = false;
            var cleaner = services.GetRequiredService<AiKnowledgeIndexCleanupStore>();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => services.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
            {
                await cleaner.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, owner.UserId, ct); return true;
            }, default));
            // 数据库删除已经执行后才取消，必须回滚载荷，而不是只验证入口的已取消令牌。
            using (var cancellation = new CancellationTokenSource())
            {
                faults.CancelCleanup = cancellation;
                try { await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cleaner.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, owner.UserId, cancellation.Token)); }
                finally { faults.CancelCleanup = null; }
            }
            Assert.AreEqual(3, await Derived()); Assert.AreEqual(audit, await Audit());
            Assert.AreEqual(releases, faults.Releases);
            faults.FailFiles = true;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId", row);
            await Replay();
            Assert.AreEqual(0, await Derived(), "删除后的派生索引必须清理");
            Assert.AreEqual(audit, await Audit());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@KnowledgeBaseId", row);
            await State("deleting");
            Assert.AreEqual("cleanup_pending", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_import WHERE DocumentId=@DocumentId", row));
            Assert.IsTrue(faults.Releases > releases);
            using (var source = await client.GetAsync(document + $"/versions/{binding.VersionId}/source")) Assert.AreEqual(HttpStatusCode.NotFound, source.StatusCode);
            Assert.IsFalse(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken.Value, default));
            Assert.IsNull(await tasks.AcquireAsync(binding, taskId, default));
            Assert.AreEqual(1, provider.Calls);

            // 模拟清理进程退出后留下的有效导入租约；到期前不抢占，过期后新请求恢复。
            faults.FailFiles = false; releases = faults.Releases;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET LeaseToken=@Lease,LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5), DocumentId = binding.DocumentId });
            await Replay(); Assert.AreEqual(releases, faults.Releases); await State("deleting");
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { Until = DateTime.UtcNow.AddMinutes(-1), DocumentId = binding.DocumentId });
            await Replay(); await State("deleted");
            Assert.AreEqual(releases + 1, faults.Releases);
            await Replay(); Assert.AreEqual(releases + 1, faults.Releases);
            await Expect(client, cleanup, HttpStatusCode.OK); Assert.AreEqual(releases + 1, faults.Releases);
            Assert.AreEqual(audit, await Audit());

            // 历史任务不要求仍有可用会话；超过一批的取消意图必须可在新请求继续处理。
            for (var index = 0; index < 26; index++)
                await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,LeaseToken,LeaseExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc) SELECT @Id,DocumentId,VersionId,@Version,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,'running',1,@Lease,@Until,CreatedAtUtc,UpdatedAtUtc FROM fn_ai_knowledge_index_task WHERE Id=@TaskId",
                    new { Id = Guid.CreateVersion7(), Version = index + 2, Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5), TaskId = taskId });
            await Expect(client, cleanup, HttpStatusCode.OK);
            Assert.AreEqual(1, await Remaining());
            faults.CandidateReaders = 0; faults.CandidatesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var competitors = await Task.WhenAll(client.PostAsync(cleanup, null), client.PostAsync(cleanup, null));
                foreach (var response in competitors) { using (response) Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync()); }
                Assert.AreEqual(2, faults.CandidateReaders);
            }
            finally { faults.CandidatesReady.TrySetResult(true); faults.CandidatesReady = null; }
            Assert.AreEqual(0, await Remaining());
            Assert.AreEqual(26, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId AND StateKey='cancelled' AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL", row));
            Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", row));
            Assert.AreEqual(audit, await Audit()); Assert.AreEqual(0, await Derived());
            await AiKnowledgeIndexCleanupRaceAssertions.VerifyAsync(factory, client, owner.UserId, scoped ? tenant.Id : null, faults);

            async Task<int> Remaining() => await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId AND StateKey='running'", row);
            async Task Replay()
            {
                using var response = await AiKnowledgeDeleteReplayAssertions.Delete(client, document, 1);
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, await response.Content.ReadAsStringAsync());
                Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@DocumentId", row));
            }
            async Task State(string expected)
            {
                using var response = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.AreEqual(expected, json.RootElement.GetProperty("state").GetString());
            }

            async Task<int> Derived() => await db.QuerySingleAsync<int>("SELECT (SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId)+(SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId)+(SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId))", row);
            async Task<string> Audit()
            {
                var parts = new List<string>();
                foreach (var table in new[] { "fn_ai_knowledge_index_session", "fn_ai_knowledge_index_batch" })
                    parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE IndexTaskId=@TaskId ORDER BY Id", row)));
                parts.Add(JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId) ORDER BY Id", row)));
                return string.Join('|', parts);
            }
        }
        // 使用同一所有者的新 Host 会话，独立证明反向范围隔离，不能靠不同所有者得到 404。
        host.DefaultRequestHeaders.Authorization = null;
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, owner.Username));
        await Expect(host, tenantCleanup!, HttpStatusCode.NotFound);
        using var crossTenantReplay = await AiKnowledgeDeleteReplayAssertions.Delete(host, tenantCleanup!.Replace("/imports/cleanup", "", StringComparison.Ordinal), 1);
        Assert.AreEqual(HttpStatusCode.NotFound, crossTenantReplay.StatusCode);
    }

    private static async Task Expect(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.PostAsync(path, null); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
