using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产写入后直接验证 SQL 守卫，避免仅由上层拒绝掩盖数据库谓词缺失。</summary>
internal static class AiKnowledgeIndexVectorAssertions
{
    internal static async Task VerifyAsync(DbConnection db, SessionBindingSnapshot session, AiKnowledgeIndexEmbeddingBatch batch,
        Func<AiKnowledgeIndexEmbeddingBatch, Task<bool>> persist, Func<Task<bool>> resume,
        Func<AiKnowledgeIndexEmbeddingBatch, Task<bool>> expireAfterWrite, Func<Task> verifyMissingManifestRecovery)
    {
        var raced = await Task.WhenAll(persist(batch), persist(batch)); Assert.IsTrue(raced.All(value => value));
        var payload = AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default);
        CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.OperationId }));
        Assert.IsTrue(await resume());
        var changed = batch.Chunks.Select(item => new AiKnowledgeEmbeddedChunk(item.Source, item.Vector.ToArray())).ToArray();
        var wrong = changed[0].Vector.ToArray(); wrong[0] = 99; changed[0] = changed[0] with { Vector = wrong };
        Assert.IsFalse(await persist(batch with { Chunks = changed }));
        CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.OperationId }));
        await verifyMissingManifestRecovery();
        // 已收费却缺失派生数据不能成为免费重发依据；内存中的原完整回执仍可再次持久保存。
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@Id", new { Id = batch.OperationId }));
        Assert.IsFalse(await resume()); Assert.IsTrue(await persist(batch)); Assert.IsTrue(await resume());

        var source = batch.Source;
        var request = await db.QuerySingleAsync<string>("SELECT RequestHash FROM fn_ai_knowledge_index_batch WHERE Id=@Id", new { Id = batch.OperationId });
        var status = await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = batch.OperationId });
        var p = new Dictionary<string, object?>
        {
            ["TaskId"] = source.TaskId, ["LeaseToken"] = source.LeaseToken, ["BatchOrdinal"] = batch.BatchOrdinal,
            ["KnowledgeBaseId"] = await db.QuerySingleAsync<Guid>("SELECT KnowledgeBaseId FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = source.DocumentId }),
            ["DocumentId"] = source.DocumentId, ["VersionId"] = source.VersionId, ["DocumentVersion"] = source.DocumentVersion,
            ["KnowledgeBaseVersion"] = source.KnowledgeBaseVersion, ["OwnerUserId"] = session.UserId, ["TenantId"] = session.TenantId,
            ["SessionId"] = session.SessionId, ["SessionKind"] = session.SessionKind, ["SecurityStamp"] = session.SecurityStamp,
            ["ActorScope"] = session.ActorScope, ["EffectiveScope"] = session.EffectiveScope,
            ["ScopeKey"] = session.TenantId is { } tenantId ? tenantId.ToString("N") : "host", ["Now"] = source.LeaseExpiresAtUtc.AddSeconds(-30),
            ["FileId"] = source.FileId, ["ContentHash"] = source.ContentHash, ["ContentType"] = source.ContentType, ["SizeBytes"] = source.SizeBytes,
            ["OperationId"] = batch.OperationId, ["InputCount"] = batch.Chunks.Count, ["RequestHash"] = request, ["ModelConfigId"] = source.ModelConfigId,
            ["ProviderKey"] = "openai_compatible", ["ModelId"] = "text-embedding-3-small", ["Dimensions"] = 1536,
            ["FormatKey"] = AiKnowledgeIndexVectorCodec.FormatKey, ["DistanceKey"] = "cosine", ["Payload"] = payload,
            ["PayloadHash"] = Convert.ToHexString(SHA256.HashData(payload)),
        };
        var sqlServer = db is Microsoft.Data.SqlClient.SqlConnection;
        var insert = sqlServer
            ? (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.InsertSqlServerTenant.Text : AiKnowledgeIndexVectorSql.InsertSqlServerHost.Text)
            : (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.InsertTenant.Text : AiKnowledgeIndexVectorSql.InsertHost.Text);
        var read = sqlServer
            ? (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.FindSqlServerTenant.Text : AiKnowledgeIndexVectorSql.FindSqlServerHost.Text)
            : (session.TenantId.HasValue ? AiKnowledgeIndexVectorSql.FindTenant.Text : AiKnowledgeIndexVectorSql.FindHost.Text);
        Assert.IsNotNull(await db.QuerySingleOrDefaultAsync(read, p));
        foreach (var key in new[] { "TaskId", "LeaseToken", "KnowledgeBaseId", "DocumentId", "VersionId", "OwnerUserId", "SessionId", "FileId" })
            await Denied(new Dictionary<string, object?>(p) { [key] = Guid.CreateVersion7() });
        foreach (var key in new[] { "ScopeKey", "SessionKind", "SecurityStamp", "ActorScope", "EffectiveScope", "ContentType", "ContentHash" })
            await Denied(new Dictionary<string, object?>(p) { [key] = "changed" });
        await Denied(new Dictionary<string, object?>(p) { ["DocumentVersion"] = 2 });
        await Denied(new Dictionary<string, object?>(p) { ["KnowledgeBaseVersion"] = 2 });
        await Denied(new Dictionary<string, object?>(p) { ["SizeBytes"] = source.SizeBytes + 1 });
        await Denied(new Dictionary<string, object?>(p) { ["Now"] = source.LeaseExpiresAtUtc });
        if (session.TenantId.HasValue)
        {
            await Denied(new Dictionary<string, object?>(p) { ["TenantId"] = Guid.CreateVersion7() });
            Assert.AreEqual(0, await db.ExecuteAsync(sqlServer ? AiKnowledgeIndexVectorSql.InsertSqlServerHost.Text : AiKnowledgeIndexVectorSql.InsertHost.Text, p));
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(sqlServer ? AiKnowledgeIndexVectorSql.FindSqlServerHost.Text : AiKnowledgeIndexVectorSql.FindHost.Text, p));
        }
        foreach (var (flip, restore) in new[]
        {
            ("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId", "UPDATE fn_ai_knowledge_document SET IsDeleted=0 WHERE Id=@DocumentId"),
            ("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", "UPDATE fn_ai_knowledge_document SET Version=1 WHERE Id=@DocumentId"),
            ("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@KnowledgeBaseId"),
            ("UPDATE fn_ai_knowledge_base SET Version=2 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET Version=1 WHERE Id=@KnowledgeBaseId"),
            ("UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId", "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=1 WHERE Id=@KnowledgeBaseId"),
            ("UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET Version=1 WHERE Id=@ModelConfigId"),
            ("UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@ModelConfigId", "UPDATE fn_ai_model_config SET IsEnabled=1 WHERE Id=@ModelConfigId"),
            ("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId", "UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE VersionId=@VersionId"),
            ("UPDATE fn_ai_operation_budget SET UsageStatus='reserved' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET UsageStatus=@OriginalStatus WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET Outcome='failed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET Outcome='succeeded' WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET ScopeKey='changed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET ScopeKey=@ScopeKey WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET RequestHash='changed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET RequestHash=@RequestHash WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET ModelConfigId=@FileId WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET ModelConfigId=@ModelConfigId WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET ProviderKey='changed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET ProviderKey=@ProviderKey WHERE Id=@OperationId"),
            ("UPDATE fn_ai_operation_budget SET ModelId='changed' WHERE Id=@OperationId", "UPDATE fn_ai_operation_budget SET ModelId=@ModelId WHERE Id=@OperationId"),
            ("UPDATE fn_ai_knowledge_index_session SET SecurityStamp='changed' WHERE IndexTaskId=@TaskId", "UPDATE fn_ai_knowledge_index_session SET SecurityStamp=@SecurityStamp WHERE IndexTaskId=@TaskId"),
            ("UPDATE fn_ai_knowledge_version SET ContentHash='changed' WHERE Id=@VersionId", "UPDATE fn_ai_knowledge_version SET ContentHash=@ContentHash WHERE Id=@VersionId"),
        })
        {
            p["OriginalStatus"] = status;
            Assert.AreEqual(1, await db.ExecuteAsync(flip, p)); await Denied(p); Assert.AreEqual(1, await db.ExecuteAsync(restore, p));
        }
        Assert.IsNotNull(await db.QuerySingleOrDefaultAsync(read, p));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", p));
        // 转发生产 INSERT 并确认其写入一行后推进时钟；必须由生产事务回滚，不是前置条件拒绝。
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", p));
        var charged = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@OperationId", p);
        Assert.IsFalse(await expireAfterWrite(batch));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", p));
        Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@OperationId", p));

        async Task Denied(Dictionary<string, object?> denied)
        {
            Assert.AreEqual(0, await db.ExecuteAsync(insert, denied));
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(read, denied));
        }
    }
}
