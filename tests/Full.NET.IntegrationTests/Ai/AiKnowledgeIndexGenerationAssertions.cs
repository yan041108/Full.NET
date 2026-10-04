using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>双库多批次实际保存与完整性拒绝；外部 Files/模型仍使用受控契约。</summary>
internal static class AiKnowledgeIndexGenerationAssertions
{
    internal static async Task VerifyAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, Guid taskId,
        Func<int, Task<AiKnowledgeIndexEmbeddingBatch?>> execute, Func<AiKnowledgeIndexEmbeddingBatch, Task<bool>> persist,
        Func<AiKnowledgeIndexTextPlan, Task<bool>> persistManifest, Func<AiKnowledgeIndexTextPlan> getPlan, Func<int> modelCalls,
        Func<Func<int, Task>?, Task<bool>> check, AiOperationBudgetAssertions.TestClock clock, DateTimeOffset expiry)
    {
        var first = await execute(0); Assert.IsNotNull(first); var plan = getPlan(); Assert.IsNotNull(plan);
        Assert.IsTrue(plan.Batches.Count > 1); Assert.IsTrue(await persist(first));
        Assert.IsFalse(await check(null)); Assert.AreEqual(1, modelCalls());
        for (var ordinal = 1; ordinal < plan.Batches.Count; ordinal++)
        {
            var batch = await execute(ordinal); Assert.IsNotNull(batch); Assert.IsTrue(await persist(batch));
        }
        Assert.IsTrue(await check(null)); Assert.AreEqual(plan.Batches.Count, modelCalls());
        var ids = new { TaskId = taskId, OperationId = first.OperationId };
        var charged = await db.QuerySingleAsync<long>("SELECT SUM(ledger.ChargedTokens) FROM fn_ai_operation_budget AS ledger INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=ledger.Id WHERE batch.IndexTaskId=@TaskId", ids);
        var bytes = AiKnowledgeIndexVectorCodec.Encode(first, 1536, default);
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", ids));
        Assert.IsFalse(await check(null)); Assert.IsTrue(await persist(first));

        // 额外尚未付费/保存的批次也不能被 INNER JOIN 隐藏，整份期望集合必须精确相等。
        var extra = Guid.CreateVersion7();
        Assert.AreEqual(1, await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@Id,@TaskId,@Ordinal,@Hash,1,@Now)",
            new { Id = extra, TaskId = taskId, Ordinal = plan.Batches.Count, Hash = new string('A', 64), Now = clock.UtcNow }));
        Assert.IsFalse(await check(null));
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_batch WHERE Id=@Id", new { Id = extra }));
        var changed = first.Chunks.ToArray(); changed[0] = changed[0] with { Source = changed[0].Source with { Offset = changed[0].Source.Offset + 1 } };
        await Replace(AiKnowledgeIndexVectorCodec.Encode(first with { Chunks = changed }, 1536, default));
        Assert.IsFalse(await check(null)); await Replace(bytes);
        await Replace(new byte[8]); Assert.IsFalse(await check(null)); await Replace(bytes);
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", ids));
        Assert.IsFalse(await check(null)); Assert.IsTrue(await persistManifest(plan));
        Assert.IsFalse(await check(async count => { if (count == 1) Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", ids)); }));
        Assert.IsTrue(await persist(first)); Assert.IsTrue(await check(null));
        // 首批已读出的行保持原值，最终回执必须发现随后发生的元数据或原始字节变化。
        Assert.IsFalse(await check(async count => { if (count == -1) Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Dimensions=3072 WHERE Id=@OperationId", ids)); }));
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Dimensions=1536 WHERE Id=@OperationId", ids));
        Assert.IsFalse(await check(async count => { if (count == -1) Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=@Id", new { Id = first.OperationId, Payload = new byte[8] })); }));
        await Replace(bytes); Assert.IsTrue(await check(null));
        var originalNow = clock.UtcNow;
        try { Assert.IsFalse(await check(count => { if (count == 2) clock.UtcNow = expiry; return Task.CompletedTask; })); }
        finally { clock.UtcNow = originalNow; }
        Assert.IsTrue(await check(null));
        Assert.AreEqual(plan.Batches.Count, modelCalls());
        Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ledger.ChargedTokens) FROM fn_ai_operation_budget AS ledger INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=ledger.Id WHERE batch.IndexTaskId=@TaskId", ids));
        Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", ids));
        await GuardSql();

        async Task Replace(byte[] payload) => Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload,PayloadHash=@Hash WHERE Id=@Id",
            new { Id = first.OperationId, Payload = payload, Hash = Convert.ToHexString(SHA256.HashData(payload)) }));
        async Task GuardSql()
        {
            var source = first.Source;
            var p = new Dictionary<string, object?> { ["TaskId"] = taskId, ["KnowledgeBaseId"] = binding.KnowledgeBaseId,
                ["DocumentId"] = binding.DocumentId, ["VersionId"] = binding.VersionId, ["DocumentVersion"] = binding.DocumentVersion,
                ["KnowledgeBaseVersion"] = binding.KnowledgeBaseVersion, ["OwnerUserId"] = binding.OwnerUserId,
                ["TenantId"] = session.TenantId, ["LeaseToken"] = source.LeaseToken, ["Now"] = clock.UtcNow,
                ["SessionId"] = session.SessionId, ["SessionKind"] = session.SessionKind, ["SecurityStamp"] = session.SecurityStamp,
                ["ActorScope"] = session.ActorScope, ["EffectiveScope"] = session.EffectiveScope,
                ["ScopeKey"] = session.TenantId?.ToString("N") ?? "host", ["FileId"] = source.FileId,
                ["ContentHash"] = source.ContentHash, ["ContentType"] = source.ContentType, ["SizeBytes"] = source.SizeBytes,
                ["ExpectedBatchCount"] = plan.Batches.Count };
            var sqlServer = db is Microsoft.Data.SqlClient.SqlConnection;
            var statement = sqlServer ? (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.ReceiptsSqlServerTenant : AiKnowledgeIndexVectorSql.ReceiptsSqlServerHost)
                : (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.ReceiptsTenant : AiKnowledgeIndexVectorSql.ReceiptsHost);
            Assert.AreEqual(plan.Batches.Count, (await db.QueryAsync(statement.Text, p)).Count());
            foreach (var key in new[] { "TaskId", "KnowledgeBaseId", "DocumentId", "VersionId", "OwnerUserId", "SessionId", "FileId", "LeaseToken" })
                Assert.AreEqual(0, (await db.QueryAsync(statement.Text, new Dictionary<string, object?>(p) { [key] = Guid.CreateVersion7() })).Count());
            foreach (var key in new[] { "SessionKind", "SecurityStamp", "ActorScope", "EffectiveScope", "ScopeKey", "ContentHash", "ContentType" })
                Assert.AreEqual(0, (await db.QueryAsync(statement.Text, new Dictionary<string, object?>(p) { [key] = "changed" })).Count());
            foreach (var key in new[] { "DocumentVersion", "KnowledgeBaseVersion", "ExpectedBatchCount" })
                Assert.AreEqual(0, (await db.QueryAsync(statement.Text, new Dictionary<string, object?>(p) { [key] = 999 })).Count());
            Assert.AreEqual(0, (await db.QueryAsync(statement.Text, new Dictionary<string, object?>(p) { ["Now"] = expiry })).Count());
            if (session.TenantId.HasValue)
                Assert.AreEqual(0, (await db.QueryAsync(statement.Text, new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() })).Count());
        }
    }

    /// <summary>先执行生产查询，再在其等待边界撤销条件；不以替代查询模拟双库结果。</summary>
    internal sealed class ReceiptQuery(IQueryExecutor inner, Func<int, Task>? afterReceipt) : IQueryExecutor
    {
        private int reads;
        private bool firstVectorRead;
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var row = await inner.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
            if (typeof(T) == typeof(AiKnowledgeIndexVectorRecord) && !firstVectorRead && afterReceipt is not null)
            { firstVectorRead = true; await afterReceipt(-1); }
            return row;
        }
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var rows = await inner.QueryAsync<T>(sql, parameters, cancellationToken);
            if (sql.Name.StartsWith("ai.knowledge_index_vector.receipts", StringComparison.Ordinal) && afterReceipt is not null) await afterReceipt(++reads);
            return rows;
        }
    }
}
