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
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await factory.InitializeAsync();
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var tenant in new[] { false, true })
        foreach (var scenario in new[] { "known", "lost", "zero", "revoked", "unmetered", "ambient", "settled_revoked", "settled_expired" })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenant);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            var bytes = Encoding.UTF8.GetBytes("双库执行来源 exact source");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            // 直接创建 AI 自有来源夹具；原始摘要覆盖整个字节流，随后经生产 Files 契约消费者读取。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash, SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = p["VersionId"] });
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", tenant ? "tenant" : "host", tenant ? "tenant" : "host");
            var authorization = new ControlledAuthorization(session);
            var files = new ControlledFiles((Guid)p["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new ControlledFactory();
            if (scenario == "lost") provider.Generate = () => throw new IOException("lost response");
            if (scenario == "zero") provider.Create = () => db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId", p);
            if (scenario == "revoked") provider.Generate = () => db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", p);
            if (scenario == "unmetered") provider.InputTokens = null;
            var task = await Use(async (tasks, _, services) => { var id = await SessionService(services, tasks).EnqueueAsync(binding, session, CancellationToken.None); return id is { } value ? await tasks.FindAsync(binding, value, CancellationToken.None) : null; }); Assert.IsNotNull(task);
            var held = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, CancellationToken.None)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
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
                    await AiKnowledgeIndexVectorAssertions.VerifyAsync(db, session, result,
                        batch => Use((tasks, _, services) => VectorStore(services, tasks).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, CancellationToken.None)),
                        () => Use((tasks, execution, services) => new AiKnowledgeIndexPersistenceService(execution, VectorStore(services, tasks))
                            .ExecuteTextBatchAsync(binding, task.Id, held.LeaseToken.Value, 0, CancellationToken.None)),
                        batch => Use((tasks, _, services) => VectorStore(services, tasks, new ExpireAfterVectorInsert(services.GetRequiredService<ICommandExecutor>(),
                            () => clock.UtcNow = batch.Source.LeaseExpiresAtUtc)).PersistAsync(binding, task.Id, held.LeaseToken.Value, batch, CancellationToken.None)));
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

            Task<AiKnowledgeIndexEmbeddingBatch?> Execute(Guid lease) => Use((_, execution, services) => scenario == "ambient"
                ? services.GetRequiredService<ICommandTransaction>().ExecuteAsync(token => execution.ExecuteTextBatchAsync(binding, task.Id, lease, 0, token), CancellationToken.None)
                : execution.ExecuteTextBatchAsync(binding, task.Id, lease, 0, CancellationToken.None));
            AiKnowledgeIndexSessionService SessionService(IServiceProvider services, AiKnowledgeIndexTaskStore tasks) => new(
                services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(),
                services.GetRequiredService<IIdGenerator>(), tasks, authorization);
            AiKnowledgeIndexVectorStore VectorStore(IServiceProvider services, AiKnowledgeIndexTaskStore tasks, ICommandExecutor? command = null) => new(
                services.GetRequiredService<IQueryExecutor>(), command ?? services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(),
                tasks, SessionService(services, tasks), authorization, services.GetRequiredService<IOptions<DatabaseOptions>>());

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
                var admission = new AiKnowledgeIndexBudgetService(query, tasks, new(query, command, tasks, ids), budget);
                var execution = new AiKnowledgeIndexEmbeddingService(query, services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IDataTransactionState>(), tasks, SessionService(services, tasks), admission, budget, authorization, files, files, files, [provider], services.GetRequiredService<AiModelBindingScope>());
                return await action(tasks, execution, services);
            }
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
    private sealed class ControlledAuthorization(SessionBindingSnapshot expected) : IBackgroundSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.AreEqual(expected, binding);
            return Task.FromResult<AuthorizedSessionActor?>(new(binding.UserId, binding.TenantId, binding.SessionId));
        }
    }
    private sealed class ControlledFiles(Guid expectedFile, Guid expectedVersion, byte[] bytes, string hash) : IHostFileReferenceReader, IHostFileContentReader, ITenantResourceFileStore
    {
        public Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Assert.AreEqual(expectedFile, fileId); return Task.FromResult<HostFileReference?>(new(fileId, bytes.Length, hash)); }
        public Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Assert.AreEqual(expectedFile, fileId); return Task.FromResult(Result<HostFileContent>.Success(new(new MemoryStream(bytes, false), "text/plain", "source.txt"))); }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        { Assert.AreEqual("ai", ownerModuleKey); Assert.AreEqual(expectedVersion, resourceId); Assert.AreEqual(expectedFile, fileId); return Task.FromResult(Result<TenantResourceFileContent>.Success(new(new MemoryStream(bytes, false), "text/plain", "source.txt"))); }
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class ControlledFactory : IAiModelClientFactory, IEmbeddingGenerator<string, Embedding<float>>
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
