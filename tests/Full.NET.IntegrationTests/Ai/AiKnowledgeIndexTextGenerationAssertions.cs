using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产双库执行跨越初始租约；原会话、恢复收费和完成重试均由实际消费者判断。</summary>
internal static class AiKnowledgeIndexTextGenerationAssertions
{
    internal static async Task VerifyAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid lease,
        Func<Func<SqlStatement, int, Task<int>>?, CancellationToken, Task<bool>> execute, Func<Guid, Task<bool>> retry,
        Func<int> calls, Func<int> opens, AiKnowledgeIndexEmbeddingAssertions.ControlledAuthorization authorization,
        AiOperationBudgetAssertions.TestClock clock, DateTimeOffset initialExpiry, bool partial)
    {
        var p = new { TaskId = taskId, DocumentId = binding.DocumentId };
        var before = calls();
        Assert.IsFalse(await retry(Guid.CreateVersion7()));
        authorization.Denied = true; Assert.IsFalse(await execute(null, default)); authorization.Denied = false;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => execute(null, cancelled.Token));
        }
        Assert.AreEqual(before, calls());
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@TaskId AND LeaseExpiresAtUtc=@Expiry",
            new { TaskId = taskId, Expiry = initialExpiry.UtcDateTime }));
        if (partial)
        {
            // 实际续租写完但确认返回零行时必须停下；恢复不会重发已存且已收费的首批。
            var renewals = 0;
            Assert.IsFalse(await execute((sql, count) => { if (sql.Name.Contains(".renew_", StringComparison.Ordinal)) { renewals++; return Task.FromResult(0); } return Task.FromResult(count); }, default));
            Assert.AreEqual(1, renewals); Assert.AreEqual(before, calls());
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
        }
        Assert.IsTrue(await execute(null, default));
        Assert.IsTrue(clock.UtcNow > initialExpiry);
        Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@TaskId AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL", p));
        var manifest = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p);
        var count = AiKnowledgeIndexTextManifestCodec.Decode(manifest, default).Summary.BatchCount;
        Assert.IsTrue(count > 1); Assert.AreEqual(count, calls());
        Assert.AreEqual((long)count, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget AS ledger INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=ledger.Id WHERE batch.IndexTaskId=@TaskId AND ledger.Outcome='succeeded' AND ledger.UsageStatus=@UsageStatus", new { TaskId = taskId, UsageStatus = partial ? "known" : "unknown" }));
        var fileOpens = opens(); clock.UtcNow = clock.UtcNow.AddMinutes(2);
        Assert.IsTrue(await execute(null, default)); Assert.AreEqual(count, calls()); Assert.AreEqual(fileOpens, opens());
        authorization.Denied = true; Assert.IsFalse(await execute(null, default)); authorization.Denied = false;
        Assert.IsFalse(await retry(Guid.CreateVersion7())); Assert.AreEqual(count, calls());
    }
}
