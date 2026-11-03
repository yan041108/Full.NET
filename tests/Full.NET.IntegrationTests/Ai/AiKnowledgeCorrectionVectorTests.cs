using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库账本、人工输入及向量恢复；模型和授权为明确替身，不访问云模型或伪造整代次验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionVectorTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Human_batches_charge_once_preserve_provenance_and_never_refire_missing_paid_payloads(DatabaseProvider provider) => VerifyAsync(provider, false);

    [TestMethod]
    public Task Restoring_paid_vectors_with_slow_authority_keeps_original_25_second_budget() => VerifyAsync(DatabaseProvider.SqlServer, true);

    private static async Task VerifyAsync(DatabaseProvider provider, bool slowAuthorityProbe)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await RecoverBeforeWrite(db, provider, connection);
        foreach (var scoped in slowAuthorityProbe ? new[] { false } : new[] { false, true })
        foreach (var scenario in slowAuthorityProbe ? new[] { "known" } : new[] { "known", "unknown", "lost", "before", "after" })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, 1, 1, (Guid)seed["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "charge-stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            const string originalText = "未经人工校对的原始 OCR"; var bytes = Encoding.UTF8.GetBytes(originalText); var hash = Hash(bytes);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var writer = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (session.TenantId is { } tenantId) writer.SetTenant(new(tenantId, "charge", "人工索引")); else writer.SetHost();
            var tenant = sp.GetRequiredService<ICurrentTenant>(); var state = sp.GetRequiredService<IDataTransactionState>();
            var query = sp.GetRequiredService<IQueryExecutor>(); var command = sp.GetRequiredService<ICommandExecutor>(); var transaction = sp.GetRequiredService<ICommandTransaction>();
            var clock = sp.GetRequiredService<IClock>(); var ids = sp.GetRequiredService<IIdGenerator>(); var database = sp.GetRequiredService<IOptions<DatabaseOptions>>();
            var ports = new AiKnowledgeCorrectionManifestTests.Ports(state, tenant, session, (Guid)seed["FileId"]!, binding.VersionId, bytes.Length, hash);
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
            var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, ports);
            var authority = new AiKnowledgeCorrectionAuthority(query, state, tenant, ports, ports, ports); var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var planning = new AiKnowledgeCorrectionIndexService(query, command, transaction, state, tenant, clock, database, tasks, sessions, ports, authority, store);
            var correction = new AiKnowledgeCorrectionStore.Binding(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, binding.OwnerUserId, 1);
            var original = AiKnowledgeParsedDocument.FromText(new(originalText, hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
            var source = await store.CreateSourceAsync(correction, Guid.CreateVersion7(), Hash("source"u8.ToArray()), original, "managed:utf8-text-v1", default); Assert.IsTrue(source.IsSuccess);
            var draft = (await store.ReadCurrentAsync(correction, original.Format, hash, bytes.Length, original.Policy, default))!.Document; var block = draft.Blocks[0];
            draft = draft.Replace(block.Id, block.Parts[0].Id, "已经人工核对的内容", default).Append(block.Id, "人工补充内容", Guid.CreateVersion7, default);
            draft = draft.Merge(block.Id, draft.Blocks[1].Id, default);
            var revision = await store.SaveAsync(correction, source.Value!.Id, 0, Guid.CreateVersion7(), Hash("revision"u8.ToArray()), "核对后索引", draft, source.Value.ParserRuntimeId, default); Assert.IsTrue(revision.IsSuccess);
            binding = binding with { DocumentVersion = 2 };
            var task = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(task);
            var held = await tasks.AcquireAsync(binding, task.Value, default, 180); Assert.IsNotNull(held?.LeaseToken); var lease = held.LeaseToken.Value;
            var budget = sp.GetRequiredService<IAiOperationBudgetStore>(); var batches = new AiKnowledgeIndexBatchStore(query, command, tasks, ids, database);
            var admission = new AiKnowledgeCorrectionBudgetService(planning, batches, budget, state);
            var model = new Model(state); if (scenario == "unknown") model.InputTokens = null;
            if (scenario == "before") model.Create = () => { ports.Denied = AiKnowledgeCorrectionPermissions.Read; return Task.CompletedTask; };
            if (scenario == "after") model.Generate = () => { ports.Denied = AiKnowledgeCorrectionPermissions.Read; return Task.CompletedTask; };
            if (scenario == "lost") model.Generate = () => throw new IOException("controlled response loss");
            var execute = new AiKnowledgeCorrectionEmbeddingService(planning, admission, budget, state, [model], sp.GetRequiredService<AiModelBindingScope>());
            var vectors = new AiKnowledgeCorrectionVectorStore(query, command, transaction, state, tenant, clock, database, tasks, planning);
            var activation = new AiKnowledgeCorrectionActivationStore(query, command, transaction, state, tenant, clock, ids, database, (tasks).Scope, authority, ports);
            var persistence = new AiKnowledgeCorrectionPersistenceService(execute, vectors, planning, sessions, activation);
            if (scenario == "known")
            {
                var automaticAdmission = new AiKnowledgeIndexBudgetService(query, tasks, batches, budget);
                Assert.IsNull(await automaticAdmission.ReserveAsync(binding, task.Value, lease, 0, [originalText], default));
                ports.Denied = AiKnowledgeCorrectionPermissions.Read;
                Assert.IsFalse(await persistence.ExecuteBatchAsync(binding, task.Value, lease, 0, default));
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Value })); ports.Denied = null;
            }
            if (scenario == "lost") await Assert.ThrowsAsync<IOException>(() => persistence.ExecuteBatchAsync(binding, task.Value, lease, 0, default));
            else Assert.AreEqual(scenario is "known" or "unknown", await persistence.ExecuteBatchAsync(binding, task.Value, lease, 0, default));
            Assert.AreEqual(scenario == "before" ? 0 : 1, model.Calls); ports.Denied = null; model.Create = null; model.Generate = null;
            if (model.Calls > 0)
            {
                var inputs = model.Inputs;
                Assert.IsNotNull(inputs);
                var input = inputs.Single();
                Assert.IsTrue(input.Contains("已经人工核对的内容", StringComparison.Ordinal));
                Assert.IsFalse(input.Contains(originalText, StringComparison.Ordinal));
            }
            var operation = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Value });
            Assert.AreEqual(scenario is "unknown" or "lost" ? "unknown" : "known", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
            Assert.AreEqual(scenario is "known" or "unknown" ? "succeeded" : scenario == "lost" ? "failed" : "cancelled", await db.QuerySingleAsync<string>("SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
            var charged = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation });
            if (scenario is "unknown" or "lost") Assert.IsTrue(charged > 3); else Assert.AreEqual(scenario == "before" ? 0L : 3L, charged);
            Assert.AreEqual(scenario is "known" or "unknown", await persistence.ExecuteBatchAsync(binding, task.Value, lease, 0, default));
            Assert.AreEqual(scenario == "before" ? 0 : 1, model.Calls, "重复、未知、失去响应或撤权回执均不能免费重发。");
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            if (scenario is not ("known" or "unknown")) continue;
            var stored = await vectors.ReadAsync(binding, task.Value, lease, 0, default); Assert.IsNotNull(stored);
            Assert.AreEqual(2, stored.Snapshot.Chunks.Single().Source.References.Count); Assert.IsNull(stored.Snapshot.Chunks[0].Source.References[1].Position);
            var prepared = await planning.ReadAsync(binding, task.Value, lease, default); Assert.IsNotNull(prepared);
            var replay = new AiKnowledgeCorrectionEmbeddingBatch(operation, 0, prepared, stored.Snapshot.Chunks.Select(c => c.Vector).ToArray());
            Assert.IsFalse(await vectors.PersistAsync(binding, task.Value, lease, replay with { OperationId = Guid.CreateVersion7() }, default));
            if (scenario != "known") continue;
            var payload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operation });
            if (slowAuthorityProbe)
            {
                // 只延迟明确替身的外部 Files 读取；原25秒调用期限保持，真实SQL、完整清单和末次撤权复核照常执行。
                ports.BeforeFileRead = token => Task.Delay(TimeSpan.FromMilliseconds(1500), token);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                try { Assert.IsNotNull(await vectors.ReadAsync(binding, task.Value, lease, 0, deadline.Token)); }
                finally { ports.BeforeFileRead = null; }
                Assert.AreEqual(1, model.Calls);
                Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
                CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operation }));
                return;
            }
            var guardedQuery = new AfterVectorReadQuery(query, () => ports.Denied = AiKnowledgeCorrectionPermissions.Read);
            var guardedVectors = new AiKnowledgeCorrectionVectorStore(guardedQuery, command, transaction, state, tenant, clock, database, tasks, planning);
            Assert.IsNull(await guardedVectors.ReadAsync(binding, task.Value, lease, 0, default), "最后SQL后的撤权不能由两行一致或内存计划掩盖。");
            Assert.AreEqual(2, guardedQuery.Reads);
            ports.Denied = null;
            var manifest = await db.QuerySingleAsync<(string FormatKey, string PayloadHash, byte[] Payload, object CreatedAtUtc)>(
                "SELECT FormatKey,PayloadHash,Payload,CreatedAtUtc FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value });
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value });
            Exception? probeFailure = null;
            try
            {
                Assert.IsNull(await vectors.ReadAsync(binding, task.Value, lease, 0, default), "真正缺失的持久清单不能用先前内存计划代替。");
                Assert.IsNull(await planning.ReadCurrentModelAsync(binding, task.Value, lease, prepared, default));
                Assert.IsFalse(await vectors.PersistAsync(binding, task.Value, lease, replay, default));
            }
            catch (Exception failure) { probeFailure = failure; throw; }
            finally
            {
                // SQL Server 保留 datetimeoffset，MySQL 保留 datetime(6)；恢复参数不能作为 object 推断成 sql_variant。
                var restore = new DynamicParameters();
                restore.Add("Id", task.Value, System.Data.DbType.Guid);
                restore.Add("FormatKey", manifest.FormatKey, System.Data.DbType.String);
                restore.Add("PayloadHash", manifest.PayloadHash, System.Data.DbType.String);
                restore.Add("Payload", manifest.Payload, System.Data.DbType.Binary);
                restore.Add("CreatedAtUtc", manifest.CreatedAtUtc, provider == DatabaseProvider.SqlServer
                    ? System.Data.DbType.DateTimeOffset : System.Data.DbType.DateTime);
                try
                {
                    await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@Id,@FormatKey,@PayloadHash,@Payload,@CreatedAtUtc)", restore);
                }
                catch (Exception restoreFailure) when (probeFailure is not null)
                { throw new AggregateException("清单拒绝验证与夹具恢复均失败，保留两条原始原因。", probeFailure, restoreFailure); }
            }
            Assert.AreEqual(1, model.Calls);
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operation }));
            if (!scoped) await RecoverAfterWrite(db, provider, connection, operation, payload);
            var bad = payload.ToArray(); bad[^1] ^= 1; await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Payload = bad, Id = operation });
            Assert.IsNull(await vectors.ReadAsync(binding, task.Value, lease, 0, default)); Assert.IsFalse(await persistence.ExecuteBatchAsync(binding, task.Value, lease, 0, default));
            Assert.AreEqual(1, model.Calls);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Payload = payload, Id = operation });
            Assert.IsNotNull(await vectors.ReadAsync(binding, task.Value, lease, 0, default));
            ports.Ready = false; Assert.IsNull(await vectors.ReadAsync(binding, task.Value, lease, 0, default)); ports.Ready = true;
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operation }));
        }
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation"));
    }

    private static async Task RecoverBeforeWrite(DbConnection db, DatabaseProvider provider, string connection)
    {
        var sql = provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sql ? "ALTER TABLE fn_ai_knowledge_index_vector DROP CONSTRAINT CK_fn_ai_knowledge_index_vector_CorrectionFormats" : "ALTER TABLE fn_ai_knowledge_index_vector DROP CHECK CK_fn_ai_knowledge_index_vector_CorrectionFormats");
        await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_index_vector ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_DocumentFormats CHECK (FormatKey<>'knowledge-correction-vector-v1')");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "335_AiKnowledgeCorrectionVectors.sql", sql); await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
    }
    private static async Task RecoverAfterWrite(DbConnection db, DatabaseProvider provider, string connection, Guid operation, byte[] payload)
    {
        var sql = provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sql ? "ALTER TABLE fn_ai_knowledge_index_vector NOCHECK CONSTRAINT CK_fn_ai_knowledge_index_vector_CorrectionFormats" : "ALTER TABLE fn_ai_knowledge_index_vector ALTER CHECK CK_fn_ai_knowledge_index_vector_CorrectionFormats NOT ENFORCED");
        for (var i = 0; i < 2; i++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "335_AiKnowledgeCorrectionVectors.sql", sql); await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operation }));
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET FormatKey='unrecognized' WHERE Id=@Id", new { Id = operation }));
        }
    }
    private sealed class AfterVectorReadQuery(IQueryExecutor inner, Action afterSecondRead) : IQueryExecutor
    {
        internal int Reads;
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var row = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (statement.Name.StartsWith("ai.knowledge_correction_vector.find_", StringComparison.Ordinal) && ++Reads == 2) afterSecondRead();
            return row;
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }

    internal sealed class Model(IDataTransactionState state) : IAiModelClientFactory, IEmbeddingGenerator<string, Embedding<float>>
    {
        public string ProviderKey => "openai_compatible"; internal int Calls; internal long? InputTokens = 3;
        internal Func<Task>? Create, Generate; internal string[]? Inputs;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken)
        { Assert.IsFalse(state.HasTransaction); if (Create is not null) await Create(); return this; }
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(state.HasTransaction); Calls++; Inputs = values.ToArray(); if (Generate is not null) await Generate();
            return new(Inputs.Select(_ => { var vector = new float[1536]; vector[0] = 1; return new Embedding<float>(vector); }))
                { Usage = InputTokens is { } count ? new UsageDetails { InputTokenCount = count, OutputTokenCount = 0 } : null };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
