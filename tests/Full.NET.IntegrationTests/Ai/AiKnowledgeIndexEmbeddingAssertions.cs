using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产双库 SQL/账本配合受控会话、Files 和模型边界；不访问云模型或伪造原生验收。</summary>
internal static class AiKnowledgeIndexEmbeddingAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock, bool wholeGeneration = false, bool activateGeneration = false, bool executeGeneration = false,
        AiKnowledgeIndexWorkerAssertions.Controls? worker = null)
    {
        await factory.InitializeAsync();
        if (!wholeGeneration && worker is null)
            await AiKnowledgeIndexPolicySnapshotAssertions.VerifyAsync(factory, clock);
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var tenant in new[] { false, true })
        foreach (var scenario in worker is not null ? new[] { "known", "unmetered", "denied", "lost", "shutdown" } : wholeGeneration ? new[] { "known", "unmetered" } : new[] { "known", "lost", "zero", "revoked", "unmetered", "ambient", "settled_revoked", "settled_expired" })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenant);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            var bytes = Encoding.UTF8.GetBytes(wholeGeneration ? string.Concat(Enumerable.Repeat("a ", 12000)) : "双库执行来源 exact source");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            // 直接创建 AI 自有来源夹具；原始摘要覆盖整个字节流，随后经生产 Files 契约消费者读取。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash, SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash, SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = p["VersionId"] });
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", tenant ? "tenant" : "host", tenant ? "tenant" : "host");
            var authorization = new ControlledAuthorization(session);
            var files = new ControlledFiles((Guid)p["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new ControlledFactory();
            if (worker is not null) { worker.Authorization = authorization; worker.Files = files; worker.Model = provider; }
            if (scenario == "lost") provider.Generate = () => throw new IOException("lost response");
            if (scenario == "zero") provider.Create = () => db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId", p);
            if (scenario == "revoked") provider.Generate = () => db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", p);
            if (scenario == "unmetered") provider.InputTokens = null;
            AiKnowledgeIndexTextPlan? wholePlan = null;
            var task = await Use(async (tasks, _, services) => { var id = await SessionService(services, tasks).EnqueueAsync(binding, session, CancellationToken.None); return id is { } value ? await tasks.FindAsync(binding, value, CancellationToken.None) : null; }); Assert.IsNotNull(task);
            var generate = provider.Generate;
            provider.Generate = async () =>
            {
                Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = task.Id }));
                if (generate is not null) await generate();
                if (executeGeneration || worker is not null) clock.UtcNow = clock.UtcNow.AddSeconds(20);
                worker?.OnGenerate?.Invoke();
            };
            if (worker is not null)
            {
                var legacySource = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, false);
                var legacyBinding = new AiKnowledgeIndexTaskBinding((Guid)legacySource["KnowledgeBaseId"]!, (Guid)legacySource["DocumentId"]!,
                    (Guid)legacySource["VersionId"]!, 1, 1, (Guid)legacySource["OwnerUserId"]!);
                await using (var legacyScope = factory.Services.CreateAsyncScope())
                {
                    legacyScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                    Assert.IsNotNull(await legacyScope.ServiceProvider.GetRequiredService<AiKnowledgeIndexTaskStore>().EnqueueAsync(legacyBinding, default));
                }
                await AiKnowledgeIndexWorkerAssertions.VerifyAsync(db, binding, task.Id, async token =>
                {
                    await using var scope = factory.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); context.SetHost();
                    var count = await scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexWorkerCoordinator>().ProcessPendingAsync(token);
                    Assert.IsTrue(scope.ServiceProvider.GetRequiredService<ICurrentTenant>().IsHost); return count;
                }, worker, clock, scenario);
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@Id AND (StateKey<>'queued' OR AttemptCount<>0)", new { Id = legacyBinding.DocumentId }));
                continue;
            }
            var held = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, CancellationToken.None)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            if (scenario == "known")
            {
                // 双库真实物化验证：最终来源判定同时携带分类/模型目的地，不用另一笔模型读取补齐。
                var projection = await Use((tasks, _, _) => tasks.ReadDispatchAsync(binding, task.Id, held.LeaseToken.Value, default));
                Assert.IsNotNull(projection); Assert.AreEqual("internal", projection.DataClassification);
                Assert.AreEqual("openai_compatible", projection.ModelProviderKey); Assert.AreEqual("text-embedding-3-small", projection.ModelId);
                Assert.AreEqual("https://api.openai.com/v1", projection.ModelEndpointBaseUrl); Assert.IsNull(projection.ModelOrganizationId);
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId='projection-test' WHERE Id=@ModelConfigId", p);
                projection = await Use((tasks, _, _) => tasks.ReadDispatchAsync(binding, task.Id, held.LeaseToken.Value, default));
                Assert.IsNotNull(projection); Assert.AreEqual("projection-test", projection.ModelOrganizationId);
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId=NULL WHERE Id=@ModelConfigId", p);
                if (!wholeGeneration)
                {
                    // Token 拒绝必须早于批次身份和账本；复用真实租约及官方模型，不能由来源无效伪造零收费。
                    var batchCount = await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Id });
                    var budgetCount = await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE ModelConfigId=@ModelConfigId", p);
                    var tokenizer = AiKnowledgeCl100kTokenizer.Get(default);
                    var excess = Enumerable.Range(0x3400, 512).Select(value => new string((char)value, 3000))
                        .First(text => tokenizer.CountTokens(text) > AiKnowledgeEmbeddingInputPolicy.MaxInputTokens);
                    await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Use((tasks, _, services) => AdmissionStore(services, tasks).ReserveAsync(binding, task.Id, held.LeaseToken.Value,
                            0, [excess], default)));
                    Assert.AreEqual(batchCount, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Id }));
                    Assert.AreEqual(budgetCount, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE ModelConfigId=@ModelConfigId", p));
                    Assert.AreEqual(0, provider.Calls);
                }
            }
            if (executeGeneration)
            {
                if (scenario == "known")
                    Assert.IsTrue(await Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution,
                        VectorStore(services, tasks), ManifestStore(services, tasks)).ExecuteTextBatchAsync(binding, task.Id, held.LeaseToken.Value, 0, default)));
                await AiKnowledgeIndexTextGenerationAssertions.VerifyAsync(db, binding, task.Id, held.LeaseToken.Value,
                    (hook, token) => Use((tasks, execution, services) =>
                    {
                        var query = services.GetRequiredService<IQueryExecutor>();
                        var command = hook is null ? services.GetRequiredService<ICommandExecutor>() : new AiKnowledgeIndexActivationAssertions.CommandHook(services.GetRequiredService<ICommandExecutor>(), hook);
                        var heldTasks = new AiKnowledgeIndexTaskStore(query, command, services.GetRequiredService<ICurrentTenant>(), clock, services.GetRequiredService<IIdGenerator>());
                        var sessions = new AiKnowledgeIndexSessionService(query, command, services.GetRequiredService<ICommandTransaction>(),
                            services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), clock,
                            services.GetRequiredService<IIdGenerator>(), heldTasks, authorization);
                        return new AiKnowledgeIndexPersistenceService(execution, VectorStore(services, heldTasks), ManifestStore(services, heldTasks),
                            new(query, command, services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IDataTransactionState>(),
                                services.GetRequiredService<ICurrentTenant>(), clock, services.GetRequiredService<IIdGenerator>(), heldTasks, authorization,
                                services.GetRequiredService<IOptions<DatabaseOptions>>()), sessions)
                            .ExecuteTextGenerationAsync(binding, task.Id, held.LeaseToken.Value, token);
                    }),
                    lease => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution,
                        VectorStore(services, tasks), ManifestStore(services, tasks), new(services.GetRequiredService<IQueryExecutor>(),
                            services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                            services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), clock,
                            services.GetRequiredService<IIdGenerator>(), tasks, authorization, services.GetRequiredService<IOptions<DatabaseOptions>>()),
                        SessionService(services, tasks)).ExecuteTextGenerationAsync(binding, task.Id, lease, default)),
                    () => provider.Calls, () => files.Opens, authorization, clock, held.LeaseExpiresAtUtc!.Value, scenario == "known");
                await Use(async (_, _, services) => { await AiKnowledgeRetrievalAssertions.VerifyAsync(services, db, binding, factory); return true; });
                continue;
            }
            if (wholeGeneration)
            {
                Guid? previousPointer = null;
                for (var generation = 0; generation < (activateGeneration ? 2 : 1); generation++)
                {
                    await AiKnowledgeIndexGenerationAssertions.VerifyAsync(db, binding, session, task.Id,
                        ordinal => Use((tasks, execution, services) => execution.ExecuteTextBatchAsync(binding, task.Id, held.LeaseToken.Value, ordinal, default,
                            (plan, token) => { wholePlan = plan; return ManifestStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, plan, token); })),
                        batch => Use((tasks, _, services) => VectorStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, default)),
                        plan => Use((tasks, _, services) => ManifestStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, plan, default)),
                        () => wholePlan!, () => provider.Calls,
                        afterReceipt => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution,
                            VectorStore(services, tasks, query: new AiKnowledgeIndexGenerationAssertions.ReceiptQuery(services.GetRequiredService<IQueryExecutor>(), afterReceipt)),
                            ManifestStore(services, tasks)).IsTextGenerationStoredAsync(binding, task.Id, held.LeaseToken.Value, default)),
                        clock, held.LeaseExpiresAtUtc!.Value);
                    if (activateGeneration)
                        await AiKnowledgeIndexActivationAssertions.VerifyAsync(db, binding, task.Id, held.LeaseToken.Value,
                            (lease, hook) => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution,
                                VectorStore(services, tasks), ManifestStore(services, tasks), new(
                                    services.GetRequiredService<IQueryExecutor>(), hook is null ? services.GetRequiredService<ICommandExecutor>()
                                        : new AiKnowledgeIndexActivationAssertions.CommandHook(services.GetRequiredService<ICommandExecutor>(), hook),
                                    services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IDataTransactionState>(),
                                    services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(), services.GetRequiredService<IIdGenerator>(),
                                    tasks, authorization, services.GetRequiredService<IOptions<DatabaseOptions>>()))
                                .ActivateTextGenerationAsync(binding, task.Id, lease, default)), () => provider.Calls, authorization,
                            clock, held.LeaseExpiresAtUtc.Value, previousPointer);
                    if (activateGeneration && generation == 0)
                    {
                        previousPointer = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId });
                        binding = binding with { DocumentVersion = binding.DocumentVersion + 1 };
                        var nextId = await Use((tasks, _, services) => SessionService(services, tasks).EnqueueAsync(binding, session, default)); Assert.IsNotNull(nextId);
                        task = await Use((tasks, _, _) => tasks.FindAsync(binding, nextId.Value, default)); Assert.IsNotNull(task);
                        held = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, default)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
                        wholePlan = null; provider.Calls = 0;
                    }
                }
                continue;
            }
            if (scenario == "ambient")
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(held.LeaseToken.Value));
                Assert.AreEqual(0, provider.Calls);
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Id }));
                continue;
            }
            if (scenario == "lost") await Assert.ThrowsExactlyAsync<IOException>(() => Execute(held.LeaseToken.Value));
            else
            {
                var result = await Execute(held.LeaseToken.Value);
                if (scenario is "known" or "unmetered")
                {
                    Assert.IsNotNull(result); Assert.AreEqual(1536, result.Chunks[0].Vector.Length);
                    Assert.IsNotNull(result.Plan); Assert.AreEqual(1, result.Plan.BatchCount); Assert.AreEqual(result.Chunks.Count, result.Plan.ChunkCount);
                    Assert.AreEqual(AiKnowledgeIndexTextPlan.FormatKey, result.Plan.FormatKey);
                    Assert.AreEqual(64, result.Plan.PlanHash.Length); Assert.AreEqual(64, result.BatchSourceHash.Length);
                    Assert.IsNotNull(wholePlan); var beforeExpiry = clock.UtcNow;
                    await AiKnowledgeTextManifestAssertions.VerifyAsync(db, binding, session, result, wholePlan,
                        plan => Use((tasks, _, services) => ManifestStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, plan, CancellationToken.None)),
                        plan => Use((tasks, _, services) => ManifestStore(services, tasks, new ExpireAfterManifestInsert(services.GetRequiredService<ICommandExecutor>(),
                            () => clock.UtcNow = result.Source.LeaseExpiresAtUtc)).PersistAsync(binding, task.Id, held.LeaseToken.Value, plan, CancellationToken.None)),
                        () => clock.UtcNow = beforeExpiry);
                    await AiKnowledgeIndexVectorAssertions.VerifyAsync(db, session, result,
                        batch => Use((tasks, _, services) => VectorStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, CancellationToken.None)),
                        () => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution, VectorStore(services, tasks), ManifestStore(services, tasks))
                            .ExecuteTextBatchAsync(binding, task.Id, held.LeaseToken.Value, 0, CancellationToken.None)),
                        batch => Use((tasks, _, services) => VectorStore(services, tasks, new ExpireAfterVectorInsert(services.GetRequiredService<ICommandExecutor>(),
                            () => clock.UtcNow = batch.Source.LeaseExpiresAtUtc)).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, CancellationToken.None)),
                        () => AiKnowledgeTextManifestAssertions.VerifyMissingManifestRecoveryAsync(db, result, wholePlan,
                            () => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution, VectorStore(services, tasks), ManifestStore(services, tasks))
                                .ExecuteTextBatchAsync(binding, task.Id, held.LeaseToken.Value, 0, CancellationToken.None)),
                            batch => Use((tasks, _, services) => VectorStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, CancellationToken.None)),
                            () => files.Opens, fail => files.ThrowOnOpen = fail, () => provider.Calls));
                    Assert.AreEqual(1, provider.Calls);
                }
                else Assert.IsNull(result);
            }
            var operation = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id AND BatchOrdinal=0", new { Id = task.Id });
            var expectedStatus = scenario is "lost" or "unmetered" ? "unknown" : "known";
            Assert.AreEqual(expectedStatus, await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
            var charged = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation });
            if (scenario == "zero") Assert.AreEqual(0L, charged);
            else if (expectedStatus == "known") Assert.AreEqual(7L, charged);
            else Assert.IsTrue(charged > 7);
            Assert.AreEqual(scenario == "zero" ? 0 : 1, provider.Calls);
            if (scenario == "unmetered")
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(30);
                held = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, CancellationToken.None)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            }
            Assert.IsNull(await Execute(held.LeaseToken.Value));
            Assert.AreEqual(scenario == "zero" ? 0 : 1, provider.Calls);
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Id }));
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));

            Task<AiKnowledgeIndexEmbeddingBatch?> Execute(Guid lease) => AiKnowledgeIndexTelemetryAssertions.VerifyAsync(task.Id,
                () => Use((tasks, execution, services) => scenario == "ambient"
                ? services.GetRequiredService<ICommandTransaction>().ExecuteAsync(token => execution.ExecuteTextBatchAsync(binding, task.Id, lease, 0, token), CancellationToken.None)
                : execution.ExecuteTextBatchAsync(binding, task.Id, lease, 0, CancellationToken.None, (plan, token) =>
                    { wholePlan = plan; return ManifestStore(services, tasks).PersistAsync(binding, task.Id, lease, plan, token); })),
                () => db.QuerySingleOrDefaultAsync<Guid?>("SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id AND BatchOrdinal=0", new { Id = task.Id }));
            AiKnowledgeIndexTextManifestStore ManifestStore(IServiceProvider services, AiKnowledgeIndexTaskStore tasks, ICommandExecutor? command = null) => new(
                services.GetRequiredService<IQueryExecutor>(), command ?? services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(),
                tasks, SessionService(services, tasks), authorization, services.GetRequiredService<IOptions<DatabaseOptions>>());
            AiKnowledgeIndexSessionService SessionService(IServiceProvider services, AiKnowledgeIndexTaskStore tasks) => new(
                services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(),
                services.GetRequiredService<IIdGenerator>(), tasks, authorization);
            AiKnowledgeIndexVectorStore VectorStore(IServiceProvider services, AiKnowledgeIndexTaskStore tasks, ICommandExecutor? command = null, IQueryExecutor? query = null) => new(
                query ?? services.GetRequiredService<IQueryExecutor>(), command ?? services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(),
                tasks, SessionService(services, tasks), authorization, services.GetRequiredService<IOptions<DatabaseOptions>>());

            // 内部执行器由此夹具按可信范围构造；准入断言复用同一构造，不能依赖未注册的 Host 服务。
            AiKnowledgeIndexBudgetService AdmissionStore(IServiceProvider services, AiKnowledgeIndexTaskStore tasks, IAiOperationBudgetStore? budget = null)
            {
                var query = services.GetRequiredService<IQueryExecutor>();
                return new(query, tasks, new(query, services.GetRequiredService<ICommandExecutor>(), tasks, services.GetRequiredService<IIdGenerator>(),
                    services.GetRequiredService<IOptions<DatabaseOptions>>()), budget ?? services.GetRequiredService<IAiOperationBudgetStore>());
            }

            async Task<T> Use<T>(Func<AiKnowledgeIndexTaskStore, AiKnowledgeIndexEmbeddingService, IServiceProvider, Task<T>> action)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
                var context = services.GetRequiredService<ICurrentTenantContextWriter>();
                if (session.TenantId is { } id) context.SetTenant(new TenantContext(id, "execution", "执行测试")); else context.SetHost();
                var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>(); var ids = services.GetRequiredService<IIdGenerator>();
                var tasks = new AiKnowledgeIndexTaskStore(query, command, services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(), ids);
                IAiOperationBudgetStore budget = services.GetRequiredService<IAiOperationBudgetStore>();
                if (scenario is "settled_revoked" or "settled_expired")
                    budget = new SettlementHookBudget(budget, async () =>
                    {
                        // 原计量已由生产账本提交，交付前的最后一次等待仍可丢失权威或到期。
                        if (scenario == "settled_revoked") await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", p);
                        else clock.UtcNow = clock.UtcNow.AddSeconds(30);
                    });
                var admission = AdmissionStore(services, tasks, budget);
                var execution = new AiKnowledgeIndexEmbeddingService(query, services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IDataTransactionState>(), tasks, SessionService(services, tasks), admission, budget, authorization, files, files, files, [provider], services.GetRequiredService<AiModelBindingScope>());
                return await action(tasks, execution, services);
            }
        }
    }
    private sealed class ExpireAfterManifestInsert(ICommandExecutor inner, Action expire) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters, CancellationToken token)
        {
            var count = await inner.ExecuteAsync(statement, parameters, token);
            if (statement.Name.StartsWith("ai.knowledge_text_manifest.insert", StringComparison.Ordinal)) { Assert.AreEqual(1, count); expire(); }
            return count;
        }
    }
    private sealed class ExpireAfterVectorInsert(ICommandExecutor inner, Action expire) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters, CancellationToken token)
        {
            var count = await inner.ExecuteAsync(statement, parameters, token);
            if (statement.Name.StartsWith("ai.knowledge_index_vector.insert", StringComparison.Ordinal))
            { Assert.AreEqual(1, count); expire(); }
            return count;
        }
    }
    private sealed class SettlementHookBudget(IAiOperationBudgetStore inner, Func<Task> afterSettlement) : IAiOperationBudgetStore
    {
        public Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default) => inner.ReserveAsync(request, cancellationToken);
        public async Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
        { await inner.SettleAsync(operationId, usage, outcome, cancellationToken); await afterSettlement(); }
    }
    internal sealed class ControlledAuthorization(SessionBindingSnapshot expected) : IBackgroundSessionAuthorization
    {
        internal bool Denied;
        internal Guid? ExpectedTenantId => expected.TenantId;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.AreEqual(expected, binding);
            return Task.FromResult<AuthorizedSessionActor?>(Denied ? null : new(binding.UserId, binding.TenantId, binding.SessionId));
        }
    }
    internal sealed class ControlledFiles(Guid expectedFile, Guid expectedVersion, byte[] bytes, string hash) : IHostFileReferenceReader, IHostFileContentReader, ITenantResourceFileStore
    {
        internal int Opens; internal bool ThrowOnOpen;
        public Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Assert.AreEqual(expectedFile, fileId); return Task.FromResult<HostFileReference?>(new(fileId, bytes.Length, hash)); }
        public Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Opens++; if (ThrowOnOpen) throw new IOException("trusted file unavailable"); Assert.AreEqual(expectedFile, fileId); return Task.FromResult(Result<HostFileContent>.Success(new(new MemoryStream(bytes, false), "text/plain", "source.txt"))); }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        { Opens++; if (ThrowOnOpen) throw new IOException("trusted file unavailable"); Assert.AreEqual("ai", ownerModuleKey); Assert.AreEqual(expectedVersion, resourceId); Assert.AreEqual(expectedFile, fileId); return Task.FromResult(Result<TenantResourceFileContent>.Success(new(new MemoryStream(bytes, false), "text/plain", "source.txt"))); }
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    internal sealed class ControlledFactory : IAiModelClientFactory, IEmbeddingGenerator<string, Embedding<float>>
    {
        public string ProviderKey => "openai_compatible";
        internal int Calls; internal long? InputTokens = 7; internal Func<Task>? Create, Generate;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken) { if (Create is not null) await Create(); return this; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; if (Generate is not null) await Generate();
            return new(values.Select(_ => { var vector = new float[1536]; vector[0] = 1; return new Embedding<float>(vector); })) { Usage = InputTokens is { } count ? new UsageDetails { InputTokenCount = count, OutputTokenCount = 0 } : null };
        }
    }
}
