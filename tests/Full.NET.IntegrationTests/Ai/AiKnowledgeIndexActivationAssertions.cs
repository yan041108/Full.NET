using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>实际双库完成态与指针提交；后续故障必须回滚两笔写入。</summary>
internal static class AiKnowledgeIndexActivationAssertions
{
    internal static async Task VerifyAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid lease,
        Func<Guid, Func<SqlStatement, int, Task<int>>?, Task<bool>> activate, Func<int> calls,
        AiKnowledgeIndexEmbeddingAssertions.ControlledAuthorization authorization, AiOperationBudgetAssertions.TestClock clock, DateTimeOffset expiry, Guid? previousPointer = null)
    {
        var p = new { TaskId = taskId, DocumentId = binding.DocumentId }; var before = calls();
        var secondWrite = previousPointer.HasValue ? "activation.update" : "activation.insert";
        Assert.IsFalse(await activate(Guid.CreateVersion7(), null));
        // 第二笔成功前拒绝和真实异常都不得遗留 completed 或指针。
        Assert.IsFalse(await activate(lease, (sql, count) => Task.FromResult(sql.Name.Contains(secondWrite, StringComparison.Ordinal) ? 0 : count)));
        await AssertRunning();
        await Assert.ThrowsExactlyAsync<IOException>(() => activate(lease, (sql, count) =>
            sql.Name.Contains(secondWrite, StringComparison.Ordinal) ? throw new IOException("pointer write failed") : Task.FromResult(count)));
        await AssertRunning();
        var now = clock.UtcNow;
        try
        {
            Assert.IsFalse(await activate(lease, (sql, count) => { if (sql.Name.Contains(secondWrite, StringComparison.Ordinal)) clock.UtcNow = expiry; return Task.FromResult(count); }));
            await AssertRunning();
        }
        finally { clock.UtcNow = now; }
        Assert.IsTrue(await activate(lease, null));
        Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@TaskId AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL", p));
        var pointer = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId AND IndexTaskId=@TaskId", p);
        if (previousPointer.HasValue) Assert.AreEqual(previousPointer.Value, pointer);
        clock.UtcNow = expiry;
        Assert.IsTrue(await activate(lease, null));
        Assert.AreEqual(pointer, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
        Assert.IsFalse(await activate(Guid.CreateVersion7(), null));
        authorization.Denied = true; Assert.IsFalse(await activate(lease, null)); authorization.Denied = false;
        var vector = await db.QuerySingleAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@TaskId AND batch.BatchOrdinal=0", p);
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=(SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId AND BatchOrdinal=0)", new { TaskId = taskId, Payload = new byte[8] });
        Assert.IsFalse(await activate(lease, null));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=(SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId AND BatchOrdinal=0)", new { TaskId = taskId, Payload = vector });
        Assert.IsTrue(await activate(lease, null));
        // 完成后的重试不能用相同摘要掩盖原来源大小或当前模型批准的漂移。
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET SizeBytes=SizeBytes+1 WHERE Id=@Id", new { Id = binding.VersionId });
        Assert.IsFalse(await activate(lease, null));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET SizeBytes=SizeBytes-1 WHERE Id=@Id", new { Id = binding.VersionId });
        var model = await db.QuerySingleAsync<Guid>("SELECT ModelConfigId FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p);
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@Id", new { Id = model });
        Assert.IsFalse(await activate(lease, null));
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET IsEnabled=1 WHERE Id=@Id", new { Id = model });
        Assert.IsTrue(await activate(lease, null));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=Version+1 WHERE Id=@DocumentId", p);
        Assert.IsFalse(await activate(lease, null)); Assert.AreEqual(before, calls());

        async Task AssertRunning()
        {
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            Assert.AreEqual(previousPointer.HasValue ? 1L : 0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            if (previousPointer.HasValue)
            {
                Assert.AreEqual(previousPointer.Value, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
                Assert.AreNotEqual(taskId, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
            }
        }
    }

    internal sealed class CommandHook(ICommandExecutor inner, Func<SqlStatement, int, Task<int>> hook) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement sql, object? parameters, CancellationToken token) =>
            await hook(sql, await inner.ExecuteAsync(sql, parameters, token));
    }
}
