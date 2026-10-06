using System.Security.Cryptography;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库事务验证结算与清单守卫；合成分页输入和受控会话不冒充解析、登录或模型派发。</summary>
internal static class AiKnowledgeIndexPdfVectorAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var scoped in new[] { false, true })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero); var now = clock.UtcNow;
            var hash = new string('a', 64); var modelId = scoped ? "text-embedding-3-large" : "text-embedding-3-small";
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType='application/pdf',ContentHash=@Hash WHERE Id=@VersionId", new { Hash = hash, VersionId = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentType='application/pdf',ContentHash=@Hash WHERE VersionId=@VersionId", new { Hash = hash, VersionId = p["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET ModelId=@Model WHERE Id=@Id", new { Model = modelId, Id = p["ModelConfigId"] });
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var auth = new Authorization(session);
            var input = new AiKnowledgePdfModelChunks(binding.KnowledgeBaseId, 1, session.TenantId, (Guid)p["ModelConfigId"]!, 1,
                "openai_compatible", modelId, hash, AiKnowledgeParsedPdf.ParserVersion, AiKnowledgeCl100kTokenizer.Version, new(),
                Enumerable.Range(0, 17).Select(i => new AiKnowledgePdfChunk(i, i / 16 + 1, i % 16 * 4, 4, "body", "body", 1)).ToArray());
            var plan = AiKnowledgeIndexPdfPlan.Create(input, default)!;
            var task = await Use(a => a.Sessions.EnqueueAsync(binding, session, default)); Assert.IsNotNull(task);
            var taskId = task.Value;
            var held = await Use(a => a.Tasks.AcquireAsync(binding, taskId, default)); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            var lease = held.LeaseToken.Value;
            Assert.IsTrue(await Use(a => a.Manifests.PersistAsync(binding, taskId, lease, plan, default)));
            var manifest = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default); var manifestHash = Hash(manifest);
            var model = await Use(a => a.Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", input.ModelConfigId)), default)); Assert.IsNotNull(model);
            p["TaskId"] = taskId; p["LeaseToken"] = lease; p["Now"] = now.UtcDateTime;
            p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
            p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope; p["ScopeKey"] = scoped ? session.TenantId!.Value.ToString("N") : "host";
            p["ContentHash"] = hash; p["ContentType"] = "application/pdf"; p["SizeBytes"] = 10; p["PdfManifestHash"] = manifestHash;
            p["ProviderKey"] = model.ProviderKey; p["ModelId"] = model.ModelId; p["Dimensions"] = scoped ? 3072 : 1536;
            p["FormatKey"] = AiKnowledgeIndexPdfVectorCodec.FormatKey; p["DistanceKey"] = "cosine";
            var sqlServer = db is Microsoft.Data.SqlClient.SqlConnection;
            var insert = sqlServer ? (scoped ? AiKnowledgeIndexVectorSql.InsertPdfSqlServerTenant : AiKnowledgeIndexVectorSql.InsertPdfSqlServerHost)
                : (scoped ? AiKnowledgeIndexVectorSql.InsertPdfTenant : AiKnowledgeIndexVectorSql.InsertPdfHost);
            var find = sqlServer ? (scoped ? AiKnowledgeIndexVectorSql.FindPdfSqlServerTenant : AiKnowledgeIndexVectorSql.FindPdfSqlServerHost)
                : (scoped ? AiKnowledgeIndexVectorSql.FindPdfTenant : AiKnowledgeIndexVectorSql.FindPdfHost);
            p["ExpectedBatchCount"] = 2;
            var receipts = sqlServer ? (scoped ? AiKnowledgeIndexVectorSql.ReceiptsPdfSqlServerTenant : AiKnowledgeIndexVectorSql.ReceiptsPdfSqlServerHost)
                : (scoped ? AiKnowledgeIndexVectorSql.ReceiptsPdfTenant : AiKnowledgeIndexVectorSql.ReceiptsPdfHost);
            long charged = 0; var operations = new List<Guid>();
            for (var ordinal = 0; ordinal < 2; ordinal++)
            {
                Assert.IsNull(await WholeProof());
                var vectors = plan.Batches[ordinal].Chunks.Select(_ => { var v = new float[scoped ? 3072 : 1536]; v[0] = 1; v[1] = BitConverter.Int32BitsToSingle(int.MinValue); return new ReadOnlyMemory<float>(v); }).ToArray();
                var request = AiEmbeddingBudgetRequest.Create(Guid.CreateVersion7(), model, plan.Batches[ordinal].Chunks.Select(c => c.EmbeddingText).ToArray());
                var batch = await Use(a => a.Batches.PrepareAsync(binding, taskId, lease, ordinal, AiOperationBudgetStore.Fingerprint(request), vectors.Length, default)); Assert.IsNotNull(batch);
                request = request with { OperationId = batch.Id }; operations.Add(batch.Id);
                await Use(a => a.Budgets.ReserveAsync(request));
                Assert.IsFalse(await Write()); Assert.IsNull(await Read());
                await Use(async a => { await a.Budgets.SettleAsync(batch.Id, ordinal == 0 ? new AiOperationUsage(1, 0) : new AiOperationUsage(null, null), "succeeded"); return true; });
                var original = AiKnowledgeIndexPdfVectorCodec.Encode(plan, ordinal, vectors, default);
                p["OperationId"] = batch.Id; p["BatchOrdinal"] = ordinal; p["RequestHash"] = batch.RequestHash; p["InputCount"] = vectors.Length;
                p["Payload"] = original; p["PayloadHash"] = Hash(original);
                var chargesBefore = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.Id });
                var raced = await Task.WhenAll(Write(), Write()); Assert.IsTrue(raced.All(value => value));
                CollectionAssert.AreEqual(original, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id }));
                var restored = await Read(); Assert.IsNotNull(restored); Assert.AreEqual(batch.Id, restored.OperationId);
                Assert.AreEqual(ordinal + 1, restored.Snapshot.Payload.Chunks[0].Source.PageNumber); Assert.AreEqual(vectors.Length, restored.Snapshot.Payload.Chunks.Count);
                Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(restored.Snapshot.Payload.Chunks[0].Vector.Span[1]));
                var altered = vectors.ToArray(); var changed = altered[0].ToArray(); changed[0] = 2; altered[0] = changed;
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, altered, default)));
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, Guid.CreateVersion7(), plan, ordinal, vectors, default)));
                // 每个拒绝条件从同一健康记录单独翻转，恢复后再证明可读。
                foreach (var (flip, restore) in new[]
                {
                    ("UsageStatus='reserved'", ordinal == 0 ? "UsageStatus='known'" : "UsageStatus='unknown'"),
                    ("Outcome='failed'", "Outcome='succeeded'"),
                    ("ScopeKey='other'", "ScopeKey=@ScopeKey"),
                    ("RequestHash='other'", "RequestHash=@RequestHash"),
                    ("ModelConfigId=@LeaseToken", "ModelConfigId=@ModelConfigId"),
                    ("ProviderKey='other'", "ProviderKey=@ProviderKey"),
                    ("ModelId='other'", "ModelId=@ModelId"),
                    ("SettledAtUtc=NULL", "SettledAtUtc=@Now")
                })
                {
                    await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + flip + " WHERE Id=@OperationId", p);
                    Assert.IsFalse(await Write(), flip); Assert.IsNull(await Read(), flip);
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p), flip); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, p), flip);
                    await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + restore + " WHERE Id=@OperationId", p);
                    Assert.IsNotNull(await Read(), restore);
                }
                foreach (var (key, value) in new (string, object)[] { ("LeaseToken", Guid.CreateVersion7()), ("TaskId", Guid.CreateVersion7()),
                    ("SessionId", Guid.CreateVersion7()), ("SecurityStamp", "other"), ("FileId", Guid.CreateVersion7()), ("ContentHash", "other"),
                    ("ContentType", "text/plain"), ("OwnerUserId", Guid.CreateVersion7()), ("KnowledgeBaseVersion", 2), ("PdfManifestHash", new string('0', 64)) })
                {
                    var denied = new Dictionary<string, object?>(p) { [key] = value };
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied), key); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied), key);
                }
                if (scoped)
                {
                    var denied = new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() };
                    Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, denied)); Assert.IsNull(await db.QuerySingleOrDefaultAsync(find.Text, denied));
                    Assert.IsNull(await db.QuerySingleOrDefaultAsync(sqlServer ? AiKnowledgeIndexVectorSql.FindPdfSqlServerHost.Text : AiKnowledgeIndexVectorSql.FindPdfHost.Text, p));
                }
                foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
                {
                    auth.DeniedPermission = permission; Assert.IsFalse(await Write()); Assert.IsNull(await Read()); auth.DeniedPermission = null;
                }
                Assert.IsFalse(await Use(a => a.Text.IsStoredAsync(binding, taskId, lease, ordinal, default)));
                var corrupt = new byte[8];
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_text_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = corrupt, Hash = Hash(corrupt), Id = taskId });
                Assert.IsFalse(await Write()); Assert.IsNull(await Read()); Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p));
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_text_manifest SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = manifest, Hash = manifestHash, Id = taskId });
                // 保留旧摘要但篡改字节，SQL 与应用都必须拒绝。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_text_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload = corrupt, Id = taskId });
                Assert.IsNull(await Read()); Assert.AreEqual(0, await db.ExecuteAsync(insert.Text, p));
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_text_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload = manifest, Id = taskId });
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id", new { Payload = corrupt, Hash = Hash(corrupt), Id = batch.Id });
                Assert.IsNull(await Read()); Assert.IsFalse(await Write());
                await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id });
                Assert.IsFalse(await Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, vectors, default), expire: true));
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.Id }));
                clock.UtcNow = now; Assert.IsTrue(await Write());
                Assert.AreEqual(chargesBefore, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.Id }));
                charged += chargesBefore;
                Task<bool> Write() => Use(a => a.Vectors.PersistAsync(binding, taskId, lease, batch.Id, plan, ordinal, vectors, default));
                Task<AiKnowledgeIndexPdfStoredBatch?> Read() => Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, ordinal, default));
            }
            var proof = await WholeProof(); Assert.IsNotNull(proof); Assert.HasCount(2, proof.Receipts);
            Assert.AreEqual(manifestHash, proof.Manifest.PayloadHash); Assert.AreEqual(plan.Summary.PlanHash, proof.Manifest.Manifest.Summary.PlanHash);
            Assert.AreEqual(2, (await db.QueryAsync(receipts.Text, p)).Count());
            var firstPayload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = operations[0] });
            foreach (var keepHash in new[] { true, false })
            {
                var broken = new byte[8];
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id",
                    new { Payload = broken, Hash = keepHash ? Hash(firstPayload) : Hash(broken), Id = operations[0] });
                Assert.IsNull(await WholeProof());
                if (keepHash) Assert.AreEqual(1, (await db.QueryAsync(receipts.Text, p)).Count());
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id",
                    new { Payload = firstPayload, Hash = Hash(firstPayload), Id = operations[0] });
                Assert.IsNotNull(await WholeProof());
            }
            foreach (var (flip, restore) in new[] { ("Outcome='failed'", "Outcome='succeeded'"),
                ("SettledAtUtc=NULL", "SettledAtUtc=@Now"), ("UsageStatus='reserved'", "UsageStatus='known'") })
            {
                await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + flip + " WHERE Id=@Id", new { Id = operations[0], Now = now.UtcDateTime });
                Assert.IsNull(await WholeProof()); Assert.AreEqual(1, (await db.QueryAsync(receipts.Text, p)).Count());
                await db.ExecuteAsync("UPDATE fn_ai_operation_budget SET " + restore + " WHERE Id=@Id", new { Id = operations[0], Now = now.UtcDateTime });
                Assert.IsNotNull(await WholeProof());
            }
            // 多出的未结算批次不能被账本 JOIN 隐藏，原始批次数必须匹配完整清单。
            var extra = await Use(a => a.Batches.PrepareAsync(binding, taskId, lease, 2, new string('b', 64), 1, default)); Assert.IsNotNull(extra);
            Assert.IsNull(await WholeProof()); Assert.AreEqual(0, (await db.QueryAsync(receipts.Text, p)).Count());
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_batch WHERE Id=@Id", new { Id = extra.Id }); Assert.IsNotNull(await WholeProof());
            foreach (var permission in new[] { "ai.knowledge_bases.read", "ai.knowledge_documents.read", "ai.knowledge_documents.source_read", "ai.knowledge_documents.upload", "ai.knowledge_documents.index_enqueue" })
            { auth.DeniedPermission = permission; Assert.IsNull(await WholeProof()); auth.DeniedPermission = null; }
            Task<AiKnowledgeIndexPdfGenerationProof?> WholeProof() => Use(a => a.Vectors.ReadWholePlanProofAsync(binding, taskId, lease, default));
            foreach (var (flip, restore) in new[]
            {
                ("UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=1 WHERE Id=@KnowledgeBaseId"),
                ("UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET Version=1 WHERE Id=@ModelConfigId"),
                ("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId", "UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE VersionId=@VersionId"),
                ("UPDATE fn_ai_knowledge_index_session SET SecurityStamp='other' WHERE IndexTaskId=@TaskId", "UPDATE fn_ai_knowledge_index_session SET SecurityStamp='stamp' WHERE IndexTaskId=@TaskId")
            })
            {
                await db.ExecuteAsync(flip, p);
                Assert.IsNull(await Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, 0, default)));
                await db.ExecuteAsync(restore, p);
                Assert.IsNotNull(await Use(a => a.Vectors.ReadSnapshotAsync(binding, taskId, lease, 0, default)));
            }
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId }));
            // 复用生产文档清理，清理派生数据不会退还或删除既有操作账本。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1,Version=2 WHERE Id=@DocumentId", p);
            await Use(async a => { await a.Cleanup.CleanupDocumentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, default); return true; });
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN @Ids", new { Ids = operations }));
            Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id IN @Ids", new { Ids = operations }));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = taskId }));
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id }));

            async Task<T> Use<T>(Func<Access, Task<T>> action, bool expire = false)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
                var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
                if (session.TenantId is { } id) writer.SetTenant(new TenantContext(id, "pdf-test", "PDF测试")); else writer.SetHost();
                var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
                var tenant = services.GetRequiredService<ICurrentTenant>(); var state = services.GetRequiredService<IDataTransactionState>();
                var transaction = services.GetRequiredService<ICommandTransaction>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
                var ids = services.GetRequiredService<IIdGenerator>(); var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
                var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, auth);
                return await action(new(tasks, sessions, new(query, command, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    new(query, expire ? new ExpireAfterInsert(command, clock) : command, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    new(query, command, tasks, ids, options), new(query, command, transaction, state, tenant, clock, tasks, sessions, auth, options),
                    query, services.GetRequiredService<IAiOperationBudgetStore>(), services.GetRequiredService<AiKnowledgeIndexCleanupStore>()));
            }
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record Access(AiKnowledgeIndexTaskStore Tasks, AiKnowledgeIndexSessionService Sessions,
        AiKnowledgeIndexPdfManifestStore Manifests, AiKnowledgeIndexPdfVectorStore Vectors, AiKnowledgeIndexBatchStore Batches,
        AiKnowledgeIndexVectorStore Text, IQueryExecutor Query, IAiOperationBudgetStore Budgets, AiKnowledgeIndexCleanupStore Cleanup);
    private sealed class Authorization(SessionBindingSnapshot expected) : IBackgroundSessionAuthorization
    {
        internal string? DeniedPermission;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AuthorizedSessionActor?>(binding != expected || permissionCode == DeniedPermission ? null : new(binding.UserId, binding.TenantId, binding.SessionId));
        }
    }
    private sealed class ExpireAfterInsert(ICommandExecutor inner, AiOperationBudgetAssertions.TestClock clock) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var count = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (statement.Name.StartsWith("ai.knowledge_index_pdf_vector.insert", StringComparison.Ordinal) && count == 1) clock.UtcNow = clock.UtcNow.AddSeconds(30);
            return count;
        }
    }
}
