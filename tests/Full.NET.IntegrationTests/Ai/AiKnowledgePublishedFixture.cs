using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>检索边界测试的发布前置数据；真实人工审核转换由 PublicationTests 单独验证。</summary>
internal static class AiKnowledgePublishedFixture
{
    private const string Snapshot = "{\"fixture\":\"retrieval-boundary\"}";

    /// <summary>回到完整载荷已结算、激活事务尚未提交的夹具阶段，不伪造已发布任务重新排队。</summary>
    internal static async Task PrepareActivationRecoveryAsync(DbConnection db, Guid task)
    {
        var p = new { Task = task, Snapshot };
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication WHERE GenerationId=@Task AND SnapshotJson<>@Snapshot", p));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_read_lease WHERE GenerationId=@Task", p));
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_publication_head WHERE PublicationId IN (SELECT Id FROM fn_ai_knowledge_publication WHERE GenerationId=@Task AND SnapshotJson=@Snapshot)", p);
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_publication WHERE GenerationId=@Task AND SnapshotJson=@Snapshot", p);
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_active_generation WHERE IndexTaskId=@Task", p);
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_generation WHERE Id=@Task", p));
    }

    internal static async Task SeedAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, DateTimeOffset clock)
    {
        var now = DateTimeOffset.UtcNow;
        var from = (clock < now ? clock : now).AddMinutes(-1);
        var to = (clock > now ? clock : now).AddDays(1);
        var id = Guid.CreateVersion7();
        var p = new { Id = id, Document = binding.DocumentId, From = from, To = to, Now = now,
            Key = Guid.CreateVersion7(), Reviewer = Guid.CreateVersion7(), Snapshot,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot))) };
        Assert.AreEqual(1, await db.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_publication
              (Id,DocumentId,GenerationId,VersionId,RequestKey,ActorUserId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,
               SnapshotJson,SnapshotHash,StateKey,EffectiveFromUtc,EffectiveToUtc,ExpectedHeadVersion,DocumentVersion,KnowledgeBaseVersion,
               ReviewerUserId,ReviewedAtUtc,CreatedAtUtc,UpdatedAtUtc,Version)
            SELECT @Id,active.DocumentId,active.Id,active.VersionId,@Key,session.ActorUserId,session.SessionId,session.SessionKind,
               session.SecurityStamp,session.ActorScope,session.EffectiveScope,@Snapshot,@Hash,'published',@From,@To,0,
               task.DocumentVersion,task.KnowledgeBaseVersion,@Reviewer,@Now,@Now,@Now,3
            FROM fn_ai_knowledge_generation AS active
            INNER JOIN fn_ai_knowledge_index_task AS task ON task.Id=active.Id
            INNER JOIN fn_ai_knowledge_index_session AS session ON session.IndexTaskId=task.Id
            INNER JOIN fn_ai_knowledge_active_generation AS candidate ON candidate.IndexTaskId=task.Id
            WHERE active.DocumentId=@Document AND active.CleanupState=0 AND task.StateKey='completed'
            """, p));
        Assert.AreEqual(1, await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_publication_head (Id,PublicationId,Version,UpdatedAtUtc) VALUES (@Document,@Id,1,@Now)", p));
    }
}
