using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>会话绑定恢复和唯一约束必须在双库验证，不依赖内存模拟唯一键。</summary>
[TestClass]
public sealed class Migration248AiKnowledgeIndexSessionRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_unjournaled_session_binding() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_unjournaled_session_binding() => RecoveryAsync(false);
    [TestMethod] public Task SqlServer_fences_frozen_identity_by_current_authority() => FencesAsync(true);
    [TestMethod] public Task MySql_fences_frozen_identity_by_current_authority() => FencesAsync(false);

    private static async Task FencesAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        foreach (var tenant in new[] { false, true })
        foreach (var mutation in new[]
        {
            "UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET Version=2 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET EmbeddingModelConfigId=@LeaseToken WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_model_config SET Version=2 WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_model_config SET TenantId=@OtherTenantId WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId",
            "UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId",
            "UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId",
            "INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@LeaseToken,@DocumentId,@FileId,2,'new.txt','text/plain','newhash',10,@OwnerUserId,@Now)",
        })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenant);
            Assert.AreEqual(1, await db.ExecuteAsync((tenant ? AiKnowledgeIndexTaskSql.EnqueueTenant : AiKnowledgeIndexTaskSql.EnqueueHost).Text, p));
            p["SessionRecordId"] = Guid.CreateVersion7(); p["SessionId"] = Guid.CreateVersion7(); p["SessionKind"] = "refresh"; p["SecurityStamp"] = "stamp"; p["ActorScope"] = "host"; p["EffectiveScope"] = tenant ? $"tenant:{(Guid)p["TenantId"]!:N}" : "host";
            var prepare = (tenant ? AiKnowledgeIndexSessionSql.InsertTenant : AiKnowledgeIndexSessionSql.InsertHost).Text;
            var find = (tenant ? AiKnowledgeIndexSessionSql.HeldTenant : AiKnowledgeIndexSessionSql.HeldHost).Text;
            var denied = new Dictionary<string, object?>(p) { ["OwnerUserId"] = Guid.CreateVersion7() };
            Assert.AreEqual(0, await db.ExecuteAsync(prepare, denied));
            Assert.AreEqual(1, await db.ExecuteAsync(prepare, p));
            Assert.AreEqual(1, await db.ExecuteAsync((tenant ? AiKnowledgeIndexTaskSql.AcquireTenant : AiKnowledgeIndexTaskSql.AcquireHost).Text, p));
            Assert.IsNotNull(await db.QuerySingleOrDefaultAsync(find, p));
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(find, denied));
            denied = new(p) { ["LeaseToken"] = Guid.CreateVersion7() };
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(find, denied));
            denied = new(p) { ["Now"] = p["LeaseExpiresAtUtc"] };
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(find, denied));
            if (tenant)
            {
                denied = new(p) { ["TenantId"] = Guid.CreateVersion7() };
                Assert.IsNull(await db.QuerySingleOrDefaultAsync(find, denied));
                Assert.IsNull(await db.QuerySingleOrDefaultAsync(AiKnowledgeIndexSessionSql.HeldHost.Text, p));
            }
            await db.ExecuteAsync(mutation, p);
            Assert.IsNull(await db.QuerySingleOrDefaultAsync(find, p), mutation);
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@TaskId", p));
        }
    }

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        p["SessionRecordId"] = Guid.CreateVersion7(); p["SessionId"] = Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_session (Id,IndexTaskId,ActorUserId,TenantId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,CreatedAtUtc) VALUES (@SessionRecordId,@TaskId,@OwnerUserId,NULL,@SessionId,'refresh','stamp','host','host',@Now)", p);
        // InnoDB 使用唯一索引支撑外键；缺失唯一约束场景仍须保留本地外键支撑索引。
        if (!sqlServer) await db.ExecuteAsync("CREATE INDEX IX_fixture_index_session_task ON fn_ai_knowledge_index_session (IndexTaskId)");
        await db.ExecuteAsync("DROP INDEX UX_fn_ai_knowledge_index_session_IndexTaskId ON " + (sqlServer ? "dbo." : "") + "fn_ai_knowledge_index_session");
        if (sqlServer)
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_session', @level2type=N'COLUMN', @level2name=N'SecurityStamp'");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(db, "fn_ai_knowledge_index_session", "UX_fn_ai_knowledge_index_session_IndexTaskId", sqlServer));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session WHERE Id=@SessionRecordId AND SessionId=@SessionId", p));
        var comment = await db.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_session') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_session'),N'SecurityStamp','ColumnId') AND name=N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_session' AND COLUMN_NAME='SecurityStamp'");
        Assert.AreEqual("冻结安全戳", comment);
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_session (Id,IndexTaskId,ActorUserId,TenantId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,CreatedAtUtc) VALUES (@LeaseToken,@TaskId,@OwnerUserId,NULL,@SessionId,'refresh','stamp','host','host',@Now)", p); });
    }

    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["184_AiModelConfig.sql", "203_PublishedModuleUuidStorage.sql", "240_AiKnowledgeBase.sql", "242_AiKnowledgeDocument.sql", "243_AiKnowledgeFileVersion.sql", "246_AiKnowledgeIndexTask.sql", "247_AiKnowledgeIndexBatch.sql", "248_AiKnowledgeIndexSession.sql"];
        var result = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(scripts, StringComparer.Ordinal)))
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(8, result.Scripts);
    }
}
