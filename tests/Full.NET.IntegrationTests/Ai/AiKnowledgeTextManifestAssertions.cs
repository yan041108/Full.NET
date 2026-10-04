using System.Data.Common;
using Dapper;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 Dapper 竞争、不可替换恢复及 SQL 守卫；不以模拟执行器证明数据库原子性。</summary>
internal static class AiKnowledgeTextManifestAssertions
{
    internal static async Task VerifyAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session,
        AiKnowledgeIndexEmbeddingBatch batch, AiKnowledgeIndexTextPlan plan, Func<AiKnowledgeIndexTextPlan, Task<bool>> persist,
        Func<AiKnowledgeIndexTextPlan, Task<bool>> expireAfterWrite, Action restoreClock)
    {
        var bytes = AiKnowledgeIndexTextManifestCodec.Encode(plan, default); var p = new Dictionary<string, object?>
        {
            ["TaskId"] = batch.Source.TaskId, ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId,
            ["VersionId"] = binding.VersionId, ["DocumentVersion"] = binding.DocumentVersion, ["KnowledgeBaseVersion"] = binding.KnowledgeBaseVersion,
            ["OwnerUserId"] = binding.OwnerUserId, ["TenantId"] = session.TenantId, ["LeaseToken"] = batch.Source.LeaseToken,
            ["Now"] = batch.Source.LeaseExpiresAtUtc.AddSeconds(-1), ["SessionId"] = session.SessionId, ["SessionKind"] = session.SessionKind,
            ["SecurityStamp"] = session.SecurityStamp, ["ActorScope"] = session.ActorScope, ["EffectiveScope"] = session.EffectiveScope,
            ["FileId"] = batch.Source.FileId, ["ContentHash"] = batch.Source.ContentHash, ["ContentType"] = batch.Source.ContentType,
            ["SizeBytes"] = batch.Source.SizeBytes, ["FormatKey"] = AiKnowledgeIndexTextManifestCodec.FormatKey,
            ["PayloadHash"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), ["Payload"] = bytes
        };
        var first = plan.Batches[0].Chunks[0]; var identity = plan.Identity;
        var changed = new AiKnowledgeModelChunks(identity.KnowledgeBaseId, identity.KnowledgeBaseVersion, identity.TenantId,
            identity.ModelConfigId, identity.ModelConfigVersion, identity.ProviderKey, identity.ModelId,
            new(identity.ContentHash, "utf8-text-v1", AiKnowledgeCl100kTokenizer.Version, identity.Format, new(),
                [first with { EmbeddingText = "changed\n" + first.EmbeddingText }]));
        var other = AiKnowledgeIndexTextPlan.Create(changed, default); Assert.IsNotNull(other);
        // 删除本用例的预派发清单，让两次生产 INSERT 竞争不存在的行，而不是仅竞争已有主键。
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        var raced = await Task.WhenAll(persist(plan), persist(plan)); Assert.IsTrue(raced.All(value => value));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        raced = await Task.WhenAll(persist(plan), persist(other)); Assert.AreEqual(1, raced.Count(value => value));
        var winner = raced[0] ? plan : other; var loser = raced[0] ? other : plan;
        var winnerBytes = AiKnowledgeIndexTextManifestCodec.Encode(winner, default);
        Assert.IsFalse(await persist(loser));
        CollectionAssert.AreEqual(winnerBytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        Assert.IsTrue(await persist(plan));
        var read = session.TenantId.HasValue ? AiKnowledgeIndexTextManifestSql.FindTenant.Text : AiKnowledgeIndexTextManifestSql.FindHost.Text;
        var insert = session.TenantId.HasValue ? AiKnowledgeIndexTextManifestSql.InsertTenant.Text : AiKnowledgeIndexTextManifestSql.InsertHost.Text;
        Assert.IsNotNull(await db.QuerySingleOrDefaultAsync(read, p));
        foreach (var key in new[] { "LeaseToken", "OwnerUserId", "SessionId", "FileId", "VersionId", "KnowledgeBaseId", "DocumentId" })
            await Denied(new Dictionary<string, object?>(p) { [key] = Guid.CreateVersion7() });
        foreach (var key in new[] { "SessionKind", "SecurityStamp", "ActorScope", "EffectiveScope", "ContentHash", "ContentType" })
            await Denied(new Dictionary<string, object?>(p) { [key] = "changed" });
        await Denied(new Dictionary<string, object?>(p) { ["Now"] = batch.Source.LeaseExpiresAtUtc });
        await Denied(new Dictionary<string, object?>(p) { ["DocumentVersion"] = 2 });
        await Denied(new Dictionary<string, object?>(p) { ["KnowledgeBaseVersion"] = 2 });
        await Denied(new Dictionary<string, object?>(p) { ["SizeBytes"] = batch.Source.SizeBytes + 1 });
        if (session.TenantId.HasValue)
        {
            await Denied(new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() });
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(AiKnowledgeIndexTextManifestSql.FindHost.Text, p));
            Assert.AreEqual(0, await db.ExecuteAsync(AiKnowledgeIndexTextManifestSql.InsertHost.Text, p));
        }
        Assert.IsFalse(await persist(other));
        CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        // 只删除本用例的合成行，观察真实 INSERT 后到期是否由生产事务完整回滚。
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        try { Assert.IsFalse(await expireAfterWrite(plan)); }
        finally { restoreClock(); }
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        Assert.IsTrue(await persist(plan));
        CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));

        async Task Denied(Dictionary<string, object?> wrong)
        { Assert.IsNull(await db.QuerySingleOrDefaultAsync(read, wrong)); Assert.AreEqual(0, await db.ExecuteAsync(insert, wrong)); }
    }

    internal static async Task VerifyMissingManifestRecoveryAsync(DbConnection db, AiKnowledgeIndexEmbeddingBatch batch,
        AiKnowledgeIndexTextPlan plan, Func<Task<bool>> resume, Func<AiKnowledgeIndexEmbeddingBatch, Task<bool>> persistVector,
        Func<int> fileOpens, Action<bool> failFiles, Func<int> modelCalls)
    {
        var ids = new { TaskId = batch.Source.TaskId, OperationId = batch.OperationId };
        var charged = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@OperationId", ids);
        var calls = modelCalls(); var opens = fileOpens();
        var manifestBytes = AiKnowledgeIndexTextManifestCodec.Encode(plan, default);
        var vectorBytes = AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default);
        await DeleteManifest();
        // 无清单时必须重读可信 Files；失败不能借已有向量跳过来源验证。
        failFiles(true);
        try { await Assert.ThrowsExactlyAsync<IOException>(() => resume()); }
        finally { failFiles(false); }
        Assert.IsTrue(fileOpens() > opens); await UnchangedBudget();
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", ids));
        opens = fileOpens(); Assert.IsTrue(await resume()); Assert.IsTrue(fileOpens() > opens);
        CollectionAssert.AreEqual(manifestBytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", ids));
        await UnchangedBudget();

        // 保持计费文本相同，只改变来源偏移：账本摘要仍相同，但完整清单归属必须拒绝。
        await DeleteManifest();
        var changed = batch.Chunks.ToArray(); changed[0] = changed[0] with { Source = changed[0].Source with { Offset = changed[0].Source.Offset + 1 } };
        await ReplaceVector(AiKnowledgeIndexVectorCodec.Encode(batch with { Chunks = changed }, 1536, default));
        opens = fileOpens(); Assert.IsFalse(await resume()); Assert.IsTrue(fileOpens() > opens); await UnchangedBudget();
        await ReplaceVector(vectorBytes);

        // 已收费但清单和向量均缺失时，重建清单也不能产生免费模型重发。
        await DeleteManifest();
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", ids));
        opens = fileOpens(); Assert.IsFalse(await resume()); Assert.IsTrue(fileOpens() > opens); await UnchangedBudget();
        CollectionAssert.AreEqual(manifestBytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", ids));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", ids));
        Assert.IsTrue(await persistVector(batch)); Assert.IsTrue(await resume()); await UnchangedBudget();

        async Task DeleteManifest() => Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", ids));
        async Task ReplaceVector(byte[] payload) => Assert.AreEqual(1, await db.ExecuteAsync(
            "UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload, PayloadHash=@PayloadHash WHERE Id=@OperationId",
            new { ids.OperationId, Payload = payload, PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)) }));
        async Task UnchangedBudget()
        {
            Assert.AreEqual(calls, modelCalls());
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@OperationId", ids));
        }
    }
}
