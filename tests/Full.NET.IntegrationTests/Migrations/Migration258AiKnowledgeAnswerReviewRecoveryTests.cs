using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>双库恢复未记账约束与说明，保留单调审核版本；删除反馈级联清理审核。</summary>
[TestClass]
public sealed class Migration258AiKnowledgeAnswerReviewRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_review_guards_and_preserves_versions() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_review_guards_and_preserves_versions() => VerifyAsync(false);
    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        string[] scripts = ["184_AiModelConfig.sql", "186_AiChatSession.sql", "203_PublishedModuleUuidStorage.sql", "205_AiChatGenerationLease.sql", "254_AiKnowledgeAnswerLineage.sql", "256_AiKnowledgeAnswerFeedback.sql", "258_AiKnowledgeAnswerReview.sql"];
        Replay(scripts);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = new { Id = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7(), MessageId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow };
        await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,CreatedAtUtc) VALUES (@SessionId,NULL,@UserId,@Id,'model','feedback',@Now)", p);
        await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,IsKnowledgeAnswer,KnowledgeLineage,CreatedAtUtc) VALUES (@MessageId,@SessionId,'assistant','answer','completed',1,'{}',@Now)", p);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_answer_feedback (Id,TenantId,SessionId,MessageId,ActorUserId,RatingKey,ReasonKey,CitationIdsJson,LineageHash,StatusKey,Version,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,NULL,@SessionId,@MessageId,@UserId,'helpful','accurate','[]',@Hash,'pending_review',1,@Now,@Now)", new { p.Id, p.SessionId, p.MessageId, p.UserId, p.Now, Hash = new string('a', 64) });
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_answer_review (Id,TenantId,FeedbackId,FeedbackVersion,LineageHash,ReviewerUserId,DecisionKey,Version,CreatedAtUtc,UpdatedAtUtc) VALUES (@ReviewId,NULL,@Id,1,@Hash,@Reviewer,'approved',7,@Now,@Now)", new { p.Id, p.Now, ReviewId = Guid.CreateVersion7(), Reviewer = Guid.CreateVersion7(), Hash = new string('a', 64) });
        foreach (var name in new[] { "CK_fn_ai_knowledge_answer_review_State", "FK_fn_ai_knowledge_answer_review_FeedbackId", "UX_fn_ai_knowledge_answer_review_FeedbackId" })
        {
            var kind = name.StartsWith("CK_", StringComparison.Ordinal) ? "CHECK" : name.StartsWith("FK_", StringComparison.Ordinal) ? "FOREIGN KEY" : "INDEX";
            // MySQL 的同列外键复用唯一索引；模拟唯一约束中断前先解除依赖，重放同时恢复两者。
            if (!sqlServer && kind == "INDEX") await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_answer_review DROP FOREIGN KEY FK_fn_ai_knowledge_answer_review_FeedbackId");
            await db.ExecuteAsync(sqlServer ? $"ALTER TABLE fn_ai_knowledge_answer_review DROP CONSTRAINT {name}" : $"ALTER TABLE fn_ai_knowledge_answer_review DROP {kind} {name}");
            Replay(["258_AiKnowledgeAnswerReview.sql"]);
            Assert.AreEqual(7, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_answer_review WHERE FeedbackId=@Id", p));
        }
        await db.ExecuteAsync(sqlServer
            ? "EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'DecisionKey'"
            : "ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN DecisionKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''");
        Replay(["258_AiKnowledgeAnswerReview.sql"]);
        Assert.AreEqual("固定批准或拒绝机器码", await db.QuerySingleAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_knowledge_answer_review'),'DecisionKey','ColumnId') AND name='MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='DecisionKey'"));
        foreach (var set in new[] { "FeedbackVersion=0", "Version=0", "DecisionKey='APPROVED'", "DecisionKey='approved '", "LineageHash=NULL", "LineageHash='invalid'" })
            await Assert.ThrowsAsync<Exception>(async () => await db.ExecuteAsync("UPDATE fn_ai_knowledge_answer_review SET " + set + " WHERE FeedbackId=@Id", p));
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_review WHERE FeedbackId=@Id", p));
        void Replay(string[] names)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(names, StringComparer.Ordinal)))
                .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && names.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(names.Length, result.Scripts);
        }
    }
}
