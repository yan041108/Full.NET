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
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.IntegrationTests.NativeAot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>以两笔真实收费索引的原子切换验证旧载荷清理、回滚和跨租户隔离。</summary>
internal static class AiKnowledgeRetiredGenerationAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider database, string connectionString, bool pdf = false, bool nativeWorker = false)
    {
        var faults = new AiKnowledgeIndexCleanupFaults();
        using var factory = new FullNetApiFactory(database, connectionString, pdf ? NativePdfKnowledgeAssertions.Settings(Environment.ProcessPath!) : null, configureTestServices: services =>
        {
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            services.Configure<AiKnowledgeCleanupWorkerOptions>(o => { o.Enabled = true; o.BatchSize = 1; o.RetiredGenerationRetentionHours = 24; });
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(AiKnowledgeCleanupHostedProcessor)).ToArray()) services.Remove(descriptor);
            faults.Configure(services);
        });
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Upload,
            AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexEnqueue, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"retirement-{Guid.NewGuid():N}", permissions);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var options = factory.Services.GetRequiredService<IOptions<AiKnowledgeCleanupWorkerOptions>>().Value;
        foreach (var scoped in new[] { false, true })
        {
            options.RetiredGenerationCleanupEnabled = false;
            if (scoped)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
                tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id));
            }
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            row["OwnerUserId"] = owner.UserId; row["TenantId"] = scoped ? tenant.Id : null;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@OwnerUserId,TenantId=@TenantId WHERE Id=@KnowledgeBaseId", row);
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@TenantId WHERE Id=@ModelConfigId", row);
            var bytes = Encoding.UTF8.GetBytes("旧代次保留窗口 recovery generation"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            if (pdf)
            {
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType='application/pdf' WHERE Id=@Id", new { Id = row["VersionId"] });
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentType='application/pdf' WHERE VersionId=@Id", new { Id = row["VersionId"] });
            }
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, (Guid)row["VersionId"]!, bytes, hash);
            var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory { InputTokens = 7 };
            var generation = AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, model);
            var oldTask = await Generate(1); var oldPointer = await Pointer();
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=Version+1 WHERE Id=@DocumentId", row);
            var newTask = await Generate(2); Assert.AreNotEqual(oldTask, newTask);
            Assert.AreEqual(newTask, (await Pointer()).IndexTaskId);
            var audit = await Audit(); Assert.AreEqual(2, await Payload(oldTask)); Assert.AreEqual(2, await Payload(newTask));

            // 独立开关与保留窗口分别阻止清理，不受外部模型或原会话影响。
            await AgePointer(); Assert.AreEqual(0, await Run());
            options.RetiredGenerationCleanupEnabled = true;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ActivatedAtUtc=@Now WHERE DocumentId=@DocumentId",
                new { Now = DateTime.UtcNow, DocumentId = row["DocumentId"] });
            Assert.AreEqual(0, await Run());
            await AgePointer();
            var cleanup = services.GetRequiredService<AiKnowledgeIndexCleanupStore>();
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            await cleanup.CleanupRetiredTaskAsync((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, Guid.CreateVersion7(), oldTask, cutoff, default);
            await cleanup.CleanupRetiredTaskAsync(Guid.CreateVersion7(), (Guid)row["DocumentId"]!, owner.UserId, oldTask, cutoff, default);
            await cleanup.CleanupRetiredTaskAsync((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, owner.UserId, newTask, cutoff, default);
            context.SetTenant(new(Guid.CreateVersion7(), "other", "无关租户"));
            await cleanup.CleanupRetiredTaskAsync((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, owner.UserId, oldTask, cutoff, default);
            if (scoped) context.SetTenant(new(tenant.Id, "acme", "测试")); else context.SetHost();
            Assert.AreEqual(2, await Payload(oldTask));

            // 状态与租约必须同时满足双库约束；验证排除运行中任务后完整恢复原终态。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',LeaseToken=@LeaseToken,LeaseExpiresAtUtc=@LeaseExpiresAtUtc WHERE Id=@Id",
                new { Id = oldTask, LeaseToken = Guid.CreateVersion7(), LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) });
            Assert.AreEqual(0, await Run()); Assert.AreEqual(2, await Payload(oldTask));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='completed',LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@Id", new { Id = oldTask });

            // 完成状态不能代替后继的真实清单、向量、回执、来源和模型证明。
            var vectorHash = await db.QuerySingleAsync<string>("SELECT PayloadHash FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = newTask });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET PayloadHash=@Hash WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Hash = new string('0', 64), Id = newTask });
            Assert.AreEqual(1, await Run()); Assert.AreEqual(2, await Payload(oldTask), "损坏后继向量时保留旧载荷"); Assert.AreEqual(0, await Run());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET PayloadHash=@Hash WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Hash = vectorHash, Id = newTask });
            var manifestHash = await db.QuerySingleAsync<string>("SELECT PayloadHash FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = newTask });
            await Protect("UPDATE fn_ai_knowledge_text_manifest SET PayloadHash=@Value WHERE Id=@Id", newTask, new string('0', 64), manifestHash);
            var receiptHash = (await Pointer()).ReceiptHash;
            await Protect("UPDATE fn_ai_knowledge_active_generation SET ReceiptHash=@Value WHERE DocumentId=@Id", (Guid)row["DocumentId"]!, new string('0', 64), receiptHash);
            await Protect("UPDATE fn_ai_model_config SET Version=@Value WHERE Id=@Id", (Guid)row["ModelConfigId"]!, 2, 1);
            await Protect("UPDATE fn_ai_knowledge_import SET StateKey=@Value WHERE VersionId=@Id", (Guid)row["VersionId"]!, "failed", "completed");

            // 来源在扫描后改变时，完整证明仍必须在执行事务内拒绝清理。
            faults.RetirementScanned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            faults.ContinueRetirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceRace = Run(); await faults.RetirementScanned.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash WHERE Id=@Id", new { Hash = new string('0', 64), Id = row["VersionId"] });
            faults.ContinueRetirement.TrySetResult(true);
            Assert.AreEqual(1, await sourceRace); Assert.AreEqual(2, await Payload(oldTask));
            faults.RetirementScanned = null; faults.ContinueRetirement = null;
            Assert.AreEqual(0, await Run());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash WHERE Id=@Id", new { Hash = hash, Id = row["VersionId"] });

            // 中途异常与取消均回滚向量和清单，游标继续推进后可重放。
            faults.InterruptCleanup = true;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(2, await Payload(oldTask)); Assert.AreEqual(0, await Run());
            faults.InterruptCleanup = false;
            using (var cancellation = new CancellationTokenSource())
            {
                faults.CancelCleanup = cancellation;
                await Assert.ThrowsAsync<OperationCanceledException>(() => cleanup.CleanupRetiredTaskAsync(
                    (Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, owner.UserId, oldTask, cutoff, cancellation.Token));
                faults.CancelCleanup = null;
            }
            Assert.AreEqual(2, await Payload(oldTask));

            // 扫描与执行之间恢复旧指针：清理必须重读锁内状态，不能依赖候选快照。
            var current = await Pointer();
            faults.RetirementScanned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            faults.ContinueRetirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Run();
            await faults.RetirementScanned.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Restore(oldPointer);
            faults.ContinueRetirement.TrySetResult(true);
            Assert.AreEqual(1, await pending); Assert.AreEqual(2, await Payload(oldTask));
            faults.RetirementScanned = null; faults.ContinueRetirement = null;
            await Restore(current); Assert.AreEqual(0, await Run());

            // 当前后继的元数据过期时保留旧载荷，待恢复完整绑定后重试。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET Version=Version+1 WHERE Id=@KnowledgeBaseId", row);
            Assert.AreEqual(0, await Run()); Assert.AreEqual(2, await Payload(oldTask));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET Version=Version-1 WHERE Id=@KnowledgeBaseId", row);
            if (nativeWorker)
            {
                Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason), reason);
                await using var native = await NativeWorkerProcessHost.StartAsync(artifact, database, connectionString, NativeAotTestTimeouts.ProcessStartup,
                    additionalSettings: new Dictionary<string, string?>
                    {
                        ["FullNet__Ai__KnowledgeCleanupWorker__Enabled"] = "true",
                        ["FullNet__Ai__KnowledgeCleanupWorker__RetiredGenerationCleanupEnabled"] = "true",
                        ["FullNet__Ai__KnowledgeCleanupWorker__RetiredGenerationRetentionHours"] = "24",
                        ["FullNet__Ai__KnowledgeCleanupWorker__PollMilliseconds"] = "250",
                    });
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (await Payload(oldTask) != 0) await Task.Delay(100, deadline.Token);
                await native.StopGracefullyAsync(TimeSpan.FromSeconds(30));
                native.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, native.ExitCode);
            }
            else Assert.AreEqual(1, await Run());
            Assert.AreEqual(0, await Payload(oldTask));
            Assert.AreEqual(2, await Payload(newTask)); Assert.AreEqual(JsonSerializer.Serialize(current), JsonSerializer.Serialize(await Pointer()));
            Assert.AreEqual(audit, await Audit()); Assert.AreEqual(pdf ? 0 : 2, model.Calls); Assert.AreEqual(0, faults.Releases);
            Assert.AreEqual(0, await Run()); Assert.AreEqual(0, await Run());

            // 实际 Worker Profile 和新进程游标再次扫描，保留活动代次与历史审计。
            using var worker = await IntegrationWorkerHostFactory.BuildAsync(database, connectionString, new Dictionary<string, string?>
            {
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:Enabled"] = "true",
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:RetiredGenerationCleanupEnabled"] = "true",
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:RetiredGenerationRetentionHours"] = "24",
            }, "AiRetirementWorkerTest", nameof(AiKnowledgeCleanupHostedProcessor));
            await worker.StartAsync();
            try
            {
                await using var workerScope = worker.Services.CreateAsyncScope();
                Assert.AreEqual(0, await workerScope.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>().ProcessPendingAsync(default));
            }
            finally { await worker.StopAsync(); }
            Assert.AreEqual(2, await Payload(newTask)); Assert.AreEqual(audit, await Audit());

            async Task<Guid> Generate(int version)
            {
                using var response = await (scoped ? tenantClient : host).PostAsync(AiKnowledgeIndexStatusApiAssertions.Path(row), null);
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var id = json.RootElement.GetProperty("taskId").GetGuid();
                var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, version, 1, owner.UserId);
                var held = await tasks.AcquireAsync(binding, id, default); Assert.IsNotNull(held);
                if (!pdf)
                {
                    var started = System.Diagnostics.Stopwatch.StartNew();
                    var generated = await generation.ExecuteTextGenerationAsync(binding, id, held.LeaseToken!.Value, default);
                    var recoveryAttempted = false;
                    // 完整已计费代次可能在激活前安全拒绝；仅允许原绑定恢复一次，不把已存证明当作激活授权。
                    if (!generated && await generation.IsTextGenerationStoredAsync(binding, id, held.LeaseToken!.Value, default))
                    {
                        recoveryAttempted = true;
                        var callsBeforeRecovery = model.Calls;
                        generated = await generation.ExecuteTextGenerationAsync(binding, id, held.LeaseToken!.Value, default);
                        Assert.AreEqual(callsBeforeRecovery, model.Calls, "已存代次恢复不能再次调用模型。");
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            recovery = "persisted-generation", database, scoped, version, callsBeforeRecovery,
                            callsAfterRecovery = model.Calls, generated,
                        }));
                    }
                    string? diagnostic = null;
                    if (!generated)
                    {
                        // 仅失败时读取受控状态；不续租、不重试生成，也不输出令牌、SQL、主体或业务载荷。
                        try
                        {
                            var current = await tasks.FindAsync(binding, id, default);
                            var sessionHeld = await services.GetRequiredService<AiKnowledgeIndexSessionService>()
                                .ReadHeldAsync(binding, id, held.LeaseToken!.Value, default);
                            var source = await tasks.ReadDispatchAsync(binding, id, held.LeaseToken!.Value, default);
                            diagnostic = JsonSerializer.Serialize(new
                            {
                                database, scoped, version, recoveryAttempted, elapsedMs = started.Elapsed.TotalMilliseconds,
                                initialLeaseExpired = held.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow,
                                state = current?.StateKey, currentLeaseExpired = current?.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow,
                                sameLease = current?.LeaseToken == held.LeaseToken,
                                sessionHeld = sessionHeld is not null, sourceAvailable = source is not null,
                            });
                        }
                        catch (Exception exception) { diagnostic = "diagnostic-read-failed:" + exception.GetType().Name; }
                    }
                    Assert.IsTrue(generated, diagnostic);
                }
                else
                {
                    // PDF 使用合成单页规划与真实结算/激活，不冒充真实 PDF 解析或云模型派发。
                    var input = new AiKnowledgePdfModelChunks(binding.KnowledgeBaseId, 1, scoped ? tenant.Id : null, (Guid)row["ModelConfigId"]!, 1,
                        "openai_compatible", "text-embedding-3-small", hash.ToLowerInvariant(), AiKnowledgeParsedPdf.ParserVersion,
                        AiKnowledgeCl100kTokenizer.Version, new(), [new(0, 1, 0, 4, "body", "body", 1)]);
                    var plan = AiKnowledgeIndexPdfPlan.Create(input, default)!; var lease = held.LeaseToken!.Value;
                    var manifests = services.GetRequiredService<AiKnowledgeIndexPdfManifestStore>();
                    Assert.IsTrue(await manifests.PersistAsync(binding, id, lease, plan, default));
                    var sourceModel = await services.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped
                        ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                        AiSqlParameters.Create(("ModelConfigId", row["ModelConfigId"])), default); Assert.IsNotNull(sourceModel);
                    var request = AiEmbeddingBudgetRequest.Create(Guid.CreateVersion7(), sourceModel, ["body"]);
                    var batch = await services.GetRequiredService<AiKnowledgeIndexBatchStore>().PrepareAsync(binding, id, lease, 0,
                        AiOperationBudgetStore.Fingerprint(request), 1, default); Assert.IsNotNull(batch);
                    var budget = services.GetRequiredService<IAiOperationBudgetStore>();
                    await budget.ReserveAsync(request with { OperationId = batch.Id }); await budget.SettleAsync(batch.Id, new(7, 0), "succeeded");
                    var vector = new float[1536]; vector[0] = 1;
                    var vectors = services.GetRequiredService<AiKnowledgeIndexPdfVectorStore>();
                    Assert.IsTrue(await vectors.PersistAsync(binding, id, lease, batch.Id, plan, 0, [new ReadOnlyMemory<float>(vector)], default));
                    var consumer = new AiKnowledgeIndexPdfPersistenceService(null!, vectors, manifests,
                        services.GetRequiredService<AiKnowledgeIndexSessionService>(), services.GetRequiredService<AiKnowledgeIndexPdfActivationStore>());
                    Assert.IsTrue(await consumer.ActivateGenerationAsync(binding, id, lease, default));
                }
                return id;
            }
            async Task Protect(string sql, Guid id, object corrupt, object original)
            {
                await db.ExecuteAsync(sql, new { Id = id, Value = corrupt });
                Assert.AreEqual(1, await Run()); Assert.AreEqual(2, await Payload(oldTask)); Assert.AreEqual(0, await Run());
                await db.ExecuteAsync(sql, new { Id = id, Value = original });
            }
            async Task<int> Run()
            {
                await using var request = factory.Services.CreateAsyncScope();
                var unrelated = Guid.CreateVersion7(); request.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(unrelated, "parent", "父作用域"));
                var count = await request.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>().ProcessPendingAsync(default);
                Assert.AreEqual(unrelated, request.ServiceProvider.GetRequiredService<ICurrentTenant>().Id); return count;
            }
            async Task<int> Payload(Guid id) => await db.QuerySingleAsync<int>("SELECT (SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id)+(SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id))", new { Id = id });
            Task AgePointer() => db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ActivatedAtUtc=@Before WHERE DocumentId=@DocumentId", new { Before = DateTime.UtcNow.AddHours(-25), DocumentId = row["DocumentId"] });
            Task<AiKnowledgeIndexActivationRecord> Pointer() => db.QuerySingleAsync<AiKnowledgeIndexActivationRecord>("SELECT Id,DocumentId,IndexTaskId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", row);
            Task Restore(AiKnowledgeIndexActivationRecord pointer) => db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET IndexTaskId=@IndexTaskId,VersionId=@VersionId,PlanHash=@PlanHash,ManifestHash=@ManifestHash,ReceiptHash=@ReceiptHash,ReceiptJson=@ReceiptJson,LeaseToken=@LeaseToken,ActivatedAtUtc=@ActivatedAtUtc WHERE DocumentId=@DocumentId", pointer);
            async Task<string> Audit()
            {
                var parts = new List<string>();
                // 测试夹具快照保留全部列，确保清理没有暗改历史收费和原会话。
                foreach (var table in new[] { "fn_ai_knowledge_index_task", "fn_ai_knowledge_index_session", "fn_ai_knowledge_index_batch" })
                    parts.Add(JsonSerializer.Serialize(await db.QueryAsync($"SELECT * FROM {table} WHERE {(table.EndsWith("task", StringComparison.Ordinal) ? "Id" : "IndexTaskId")} IN (@OldTask,@NewTask) ORDER BY Id", new { OldTask = oldTask, NewTask = newTask })));
                parts.Add(JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId IN (@OldTask,@NewTask)) ORDER BY Id", new { OldTask = oldTask, NewTask = newTask })));
                return string.Join('|', parts);
            }
        }
    }
}
