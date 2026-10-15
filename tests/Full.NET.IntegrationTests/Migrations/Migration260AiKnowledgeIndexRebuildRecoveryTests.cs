using System.Data.Common;
using System.Text.Json;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>重建迁移可从未记账的列、约束及说明中断恢复，旧任务保持零代次且不改写历史。</summary>
[TestClass]
public sealed class Migration260AiKnowledgeIndexRebuildRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_generation_structure_without_rewriting_old_tasks() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_generation_structure_without_rewriting_old_tasks() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connection = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        string[] original = ["184_AiModelConfig.sql", "203_PublishedModuleUuidStorage.sql", "240_AiKnowledgeBase.sql", "242_AiKnowledgeDocument.sql", "243_AiKnowledgeFileVersion.sql", "246_AiKnowledgeIndexTask.sql"];
        Replay(original, true);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connection);
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,
                ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,FailureCode,LeaseToken,LeaseExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@TaskId,@DocumentId,@VersionId,1,1,@ModelConfigId,1,'failed',3,@FailureCode,NULL,NULL,@Now,@Now)
            """, p);
        var before = await OldTask();
        // 模拟仅第一步 DDL 已提交但 DbUp 未记账，后续重放须补齐其余结构。
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_ai_knowledge_index_task ADD Generation int NOT NULL CONSTRAINT DF_fn_ai_knowledge_index_task_Generation DEFAULT (0) WITH VALUES"
            : "ALTER TABLE fn_ai_knowledge_index_task ADD COLUMN Generation int NOT NULL DEFAULT 0");
        Replay(["260_AiKnowledgeIndexRebuild.sql"]);
        Assert.AreEqual(before, await OldTask());
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT Generation FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        Assert.AreEqual(0, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(db, "fn_ai_knowledge_index_task", "UQ_fn_ai_knowledge_index_task_Source", sqlServer));
        foreach (var index in new[] { "UQ_fn_ai_knowledge_index_task_SourceGeneration", "UX_fn_ai_knowledge_index_task_RebuildRequest" })
            Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(db, "fn_ai_knowledge_index_task", index, sqlServer));
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_ai_knowledge_index_task NOCHECK CONSTRAINT CK_fn_ai_knowledge_index_task_Generation"
            : "ALTER TABLE fn_ai_knowledge_index_task ALTER CHECK CK_fn_ai_knowledge_index_task_Generation NOT ENFORCED");
        await db.ExecuteAsync(sqlServer
            ? "EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'Generation'"
            : "ALTER TABLE fn_ai_knowledge_index_task MODIFY COLUMN Generation int NOT NULL COMMENT ''");
        Replay(["260_AiKnowledgeIndexRebuild.sql"]); Replay(["260_AiKnowledgeIndexRebuild.sql"]);
        Assert.AreEqual(before, await OldTask());
        Assert.AreEqual("同一来源快照内的单调重建代次；既有任务为零", await db.QuerySingleAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_knowledge_index_task'),'Generation','ColumnId') AND name='MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='Generation'"));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET Generation=1 WHERE Id=@TaskId", p));
        Assert.AreEqual(before, await OldTask());
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,
                ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@LeaseToken,@DocumentId,@VersionId,1,1,@ModelConfigId,1,'queued',0,@Now,@Now)
            """, p));
        var next = Guid.CreateVersion7(); var key = Guid.CreateVersion7();
        Assert.AreEqual(1, await db.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,
                ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc,Generation,RebuildRequestId,RebuildPreviousTaskId)
            VALUES (@Next,@DocumentId,@VersionId,1,1,@ModelConfigId,1,'queued',0,@Now,@Now,1,@Key,@TaskId)
            """, new { Next = next, Key = key, DocumentId = p["DocumentId"], VersionId = p["VersionId"], ModelConfigId = p["ModelConfigId"], Now = p["Now"], TaskId = p["TaskId"] }));
        Replay(["260_AiKnowledgeIndexRebuild.sql"]); Assert.AreEqual(before, await OldTask());
        Assert.AreEqual(key, await db.QuerySingleAsync<Guid>("SELECT RebuildRequestId FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = next }));

        async Task<string> OldTask() => JsonSerializer.Serialize(await db.QuerySingleAsync("SELECT Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,FailureCode,LeaseToken,LeaseExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        void Replay(string[] scripts, bool compatibility = false)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            if (compatibility) builder = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(scripts, StringComparer.Ordinal)));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(scripts.Length, result.Scripts);
        }
    }
}
