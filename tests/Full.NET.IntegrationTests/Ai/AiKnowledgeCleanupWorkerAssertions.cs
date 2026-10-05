using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实扫描与持久状态恢复；外部故障仅注入 Files 和既有 SQL 执行器边界。</summary>
internal static class AiKnowledgeCleanupWorkerAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider database, string connectionString)
    {
        var faults = new AiKnowledgeIndexCleanupFaults();
        using var factory = new FullNetApiFactory(database, connectionString, configureTestServices: services =>
        {
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            services.Configure<AiKnowledgeCleanupWorkerOptions>(o => { o.Enabled = true; o.BatchSize = 1; });
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AiKnowledgeCleanupHostedProcessor)).ToArray()) services.Remove(descriptor);
            faults.Configure(services);
        });
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Upload,
            AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexEnqueue, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"cleanup-worker-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        foreach (var scoped in new[] { false, true })
        {
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
            }
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            row["OwnerUserId"] = owner.UserId; row["TenantId"] = scoped ? tenant.Id : null;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@OwnerUserId,TenantId=@TenantId WHERE Id=@KnowledgeBaseId", row);
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@TenantId WHERE Id=@ModelConfigId", row);
            var bytes = Encoding.UTF8.GetBytes("后台恢复正文 cleanup worker"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            using (var enqueue = await (scoped ? tenantClient : host).PostAsync(AiKnowledgeIndexStatusApiAssertions.Path(row), null))
            {
                Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, await enqueue.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await enqueue.Content.ReadAsStringAsync()); row["TaskId"] = json.RootElement.GetProperty("taskId").GetGuid();
            }
            var taskId = (Guid)row["TaskId"]!;
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner.UserId);
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>(); var held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory { InputTokens = scoped ? null : 7 };
            Assert.IsTrue(await AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, model).ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            var audit = await Audit();
            Assert.AreEqual(0, await Run()); Assert.AreEqual(3, await Payload());
            faults.VersionId = binding.VersionId; faults.TenantId = scoped ? tenant.Id : null;
            // 同一时间下仍由 Kind/Id 稳定分页；不能依赖创建时钟的先后偶然性。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET CreatedAtUtc=(SELECT CreatedAtUtc FROM fn_ai_knowledge_index_task WHERE Id=@TaskId) WHERE DocumentId=@DocumentId", row);
            // 模拟 DELETE 事务已提交而进程尚未开始任何清理；不使用 HTTP 清理接口推进。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId", row);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId", row);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending',LeaseToken=@LeaseToken,LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { LeaseToken = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5), DocumentId = binding.DocumentId });
            faults.InterruptCleanup = true;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(3, await Payload());
            Assert.AreEqual(0, await Run(), "扫描到末尾回绕，不重复占用满批首项");
            var releases = faults.Releases;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { Until = DateTime.UtcNow.AddMinutes(-1), DocumentId = binding.DocumentId });
            faults.FailFiles = true;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(1, await Run());
            Assert.AreEqual(3, await Payload()); Assert.AreEqual(releases + 1, faults.Releases, "索引失败不能饿死后面的补偿操作");
            Assert.AreEqual(0, await Run());
            faults.InterruptCleanup = false; faults.FailFiles = false;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(0, await Payload());
            // 在旧操作上留下有效租约，真正的 Worker Profile 启动后必须等到租约过期。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET LeaseToken=@Lease,LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5), DocumentId = binding.DocumentId });
            using var worker = await IntegrationWorkerHostFactory.BuildAsync(database, connectionString, new Dictionary<string, string?>
            {
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:Enabled"] = "true",
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:BatchSize"] = "1",
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:PollMilliseconds"] = "250",
            }, "AiCleanupWorkerTest", nameof(AiKnowledgeCleanupHostedProcessor));
            await using (var resolution = worker.Services.CreateAsyncScope())
            {
                resolution.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                _ = resolution.ServiceProvider.GetRequiredService<AiKnowledgeImportCoordinator>();
            }
            await worker.StartAsync();
            try
            {
                await using (var workerScope = worker.Services.CreateAsyncScope())
                {
                    Assert.IsFalse(workerScope.ServiceProvider.GetRequiredService<IOptions<AiKnowledgeIndexWorkerOptions>>().Value.Enabled);
                    Assert.AreEqual(0, await workerScope.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>().ProcessPendingAsync(default));
                }
                Assert.AreEqual("cleanup_pending", await ImportState());
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET LeaseExpiresAtUtc=@Until WHERE DocumentId=@DocumentId", new { Until = DateTime.UtcNow.AddMinutes(-1), DocumentId = binding.DocumentId });
                await Eventually(async () => await ImportState() == "failed");
                // 无冻结会话的历史任务在禁用目录中仍须取消；跨轮和跨实例处理不修改终态任务或账本。
                for (var index = 0; index < 26; index++)
                    await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,LeaseToken,LeaseExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc) SELECT @Id,DocumentId,VersionId,@Version,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,'running',1,@Lease,@Until,CreatedAtUtc,UpdatedAtUtc FROM fn_ai_knowledge_index_task WHERE Id=@TaskId",
                        new { Id = Guid.CreateVersion7(), Version = index + 2, Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5), TaskId = taskId });
                using var restarted = await IntegrationWorkerHostFactory.BuildAsync(database, connectionString, new Dictionary<string, string?>
                {
                    [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:Enabled"] = "true",
                    [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:BatchSize"] = "4",
                    [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:PollMilliseconds"] = "250",
                }, "AiCleanupWorkerRestartTest", nameof(AiKnowledgeCleanupHostedProcessor));
                await restarted.StartAsync();
                try { await Eventually(async () => await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId AND StateKey='cancelled' AND LeaseToken IS NULL", row) == 26); }
                finally { await restarted.StopAsync(); }
                Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", row));
            }
            finally { await worker.StopAsync(); }
            Assert.AreEqual(0, await Run()); Assert.AreEqual(audit, await Audit()); Assert.AreEqual(1, model.Calls);

            async Task<int> Run()
            {
                await using var request = factory.Services.CreateAsyncScope();
                var parent = request.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                var unrelated = Guid.CreateVersion7(); parent.SetTenant(new(unrelated, "parent", "父作用域"));
                var count = await request.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>().ProcessPendingAsync(default);
                Assert.AreEqual(unrelated, request.ServiceProvider.GetRequiredService<ICurrentTenant>().Id); return count;
            }
            async Task<int> Payload() => await db.QuerySingleAsync<int>("SELECT (SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId)+(SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId)+(SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId))", row);
            async Task<string> ImportState() => await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_import WHERE DocumentId=@DocumentId", row);
            async Task<string> Audit()
            {
                var parts = new List<string>();
                foreach (var table in new[] { "fn_ai_knowledge_index_session", "fn_ai_knowledge_index_batch" }) parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE IndexTaskId=@TaskId ORDER BY Id", row)));
                parts.Add(JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId) ORDER BY Id", row)));
                return string.Join('|', parts);
            }
        }
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, deadline.Token);
    }
}
