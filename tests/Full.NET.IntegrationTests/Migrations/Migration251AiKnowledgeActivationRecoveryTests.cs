using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;
using System.Data;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>逐阶段未记账恢复先扩展约束，再移除旧约束，不自动激活旧任务。</summary>
[TestClass]
public sealed class Migration251AiKnowledgeActivationRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_unjournaled_activation() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_unjournaled_activation() => RecoveryAsync(false);
    [TestMethod] public Task SqlServer_completion_blocks_late_generation_writes() => LateWritesAsync(true);
    [TestMethod] public Task MySql_completion_blocks_late_generation_writes() => LateWritesAsync(false);

    private static async Task LateWritesAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString);
        Migration250AiKnowledgeTextManifestRecoveryTests.Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var migration = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
            name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("211_AiOperationBudget.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).Build().PerformUpgrade();
        Assert.IsTrue(migration.Successful, migration.Error?.ToString());
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        await db.OpenAsync();
        if (sqlServer) await db.ExecuteAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
        foreach (var tenant in new[] { false, true })
        foreach (var kind in new[] { "batch", "manifest", "vector" })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenant);
            Assert.AreEqual(1, await db.ExecuteAsync((tenant ? AiKnowledgeIndexTaskSql.EnqueueTenant : AiKnowledgeIndexTaskSql.EnqueueHost).Text, p));
            p["LeaseToken"] = Guid.CreateVersion7(); p["Expiry"] = ((DateTime)p["Now"]!).AddMinutes(5);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',LeaseToken=@LeaseToken,LeaseExpiresAtUtc=@Expiry WHERE Id=@TaskId", p);
            p["SessionId"] = Guid.CreateVersion7(); p["SessionKind"] = "refresh"; p["SecurityStamp"] = "stamp"; p["ActorScope"] = tenant ? "tenant" : "host"; p["EffectiveScope"] = p["ActorScope"];
            p["OperationId"] = Guid.CreateVersion7(); p["BatchOrdinal"] = 0; p["RequestHash"] = new string('A', 64); p["InputCount"] = 1;
            p["ProviderKey"] = "openai_compatible"; p["ModelId"] = "text-embedding-3-small"; p["Dimensions"] = 1536; p["DistanceKey"] = "cosine";
            p["PayloadHash"] = new string('B', 64); p["Payload"] = new byte[16]; p["ContentHash"] = "abcdef"; p["ContentType"] = "text/plain"; p["SizeBytes"] = 10;
            p["ScopeKey"] = tenant ? ((Guid)p["TenantId"]!).ToString("N") : "host";
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_session (Id,IndexTaskId,ActorUserId,TenantId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,CreatedAtUtc) VALUES (@SessionId,@TaskId,@OwnerUserId,@TenantId,@SessionId,@SessionKind,@SecurityStamp,@ActorScope,@EffectiveScope,@Now)", p);
            if (kind == "vector")
            {
                await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@OperationId,@TaskId,0,@RequestHash,1,@Now)", p);
                await db.ExecuteAsync("INSERT INTO fn_ai_operation_budget (Id,ScopeKey,ModelConfigId,ProviderKey,ModelId,RequestHash,QuotaMonthKey,ReservedTokens,ChargedTokens,UsageStatus,Outcome,LegacyTracked,CreatedAtUtc) VALUES (@OperationId,@ScopeKey,@ModelConfigId,@ProviderKey,@ModelId,@RequestHash,'2026-10',1,1,'known','succeeded',0,@Now)", p);
            }
            p["FormatKey"] = kind == "manifest" ? "knowledge-text-manifest-v1" : "knowledge-vector-batch-v1";
            var sql = kind switch
            {
                "batch" => sqlServer ? (tenant ? AiKnowledgeIndexBatchSql.PrepareSqlServerTenant : AiKnowledgeIndexBatchSql.PrepareSqlServerHost)
                    : (tenant ? AiKnowledgeIndexBatchSql.PrepareTenant : AiKnowledgeIndexBatchSql.PrepareHost),
                "manifest" => sqlServer ? (tenant ? AiKnowledgeIndexTextManifestSql.InsertSqlServerTenant : AiKnowledgeIndexTextManifestSql.InsertSqlServerHost)
                    : (tenant ? AiKnowledgeIndexTextManifestSql.InsertTenant : AiKnowledgeIndexTextManifestSql.InsertHost),
                _ => sqlServer ? (tenant ? AiKnowledgeIndexVectorSql.InsertSqlServerTenant : AiKnowledgeIndexVectorSql.InsertSqlServerHost)
                    : (tenant ? AiKnowledgeIndexVectorSql.InsertTenant : AiKnowledgeIndexVectorSql.InsertHost)
            };
            // 两个真实 ReadCommitted 连接；RCSI 开启后也不能先读 running，再越过完成锁插入。
            await using var writer = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString); await writer.OpenAsync();
            await using var writing = await writer.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await using var blocker = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString); await blocker.OpenAsync();
            await using var blocking = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await blocker.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='completed',LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@TaskId", p, blocking);
            var pending = writer.ExecuteAsync(sql.Text, p, writing, commandTimeout: 15);
            Assert.AreNotSame(pending, await Task.WhenAny(pending, Task.Delay(300)), "Late writer must wait for the task completion lock.");
            await blocking.CommitAsync();
            Assert.AreEqual(0, await pending, "A completed task cannot accept a stale running snapshot.");
            await writing.CommitAsync();
        }
    }

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString);
        Migration250AiKnowledgeTextManifestRecoveryTests.Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation"));
        // 模拟新旧约束同时存在的中断点；恢复须移除旧约束但保留新生命周期守卫。
        await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_index_task ADD CONSTRAINT CK_fn_ai_knowledge_index_task_State CHECK (StateKey IN ('queued','running','failed','cancelled'))");
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='completed' WHERE Id=@TaskId", p));
        await Assert.ThrowsAsync<System.Data.Common.DbException>(async () => { await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='invalid' WHERE Id=@TaskId", p); });
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='queued' WHERE Id=@TaskId", p));
        await db.ExecuteAsync(sqlServer ? "ALTER TABLE fn_ai_knowledge_index_task DROP CONSTRAINT CK_fn_ai_knowledge_index_task_Lifecycle" : "ALTER TABLE fn_ai_knowledge_index_task DROP CHECK CK_fn_ai_knowledge_index_task_Lifecycle");
        Replay(sqlServer, connectionString);
        p["Id"] = Guid.CreateVersion7(); p["LeaseToken"] = Guid.CreateVersion7(); p["Hash"] = new string('A', 64);
        const string insert = "INSERT INTO fn_ai_knowledge_active_generation (Id,DocumentId,IndexTaskId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc) VALUES (@Id,@DocumentId,@TaskId,@VersionId,@Hash,@Hash,@Hash,'[]',@LeaseToken,@Now)";
        Assert.AreEqual(1, await db.ExecuteAsync(insert, p));
        if (sqlServer)
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'ReceiptJson'");
        else
            await db.ExecuteAsync("ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN ReceiptJson mediumtext CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''");
        foreach (var column in new[] { "DocumentId", "IndexTaskId" })
        {
            var unique = "UQ_fn_ai_knowledge_active_generation_" + column;
            var support = "IX_fn_ai_knowledge_active_generation_" + column;
            // MySQL 外键需要一个仍存在的支持索引，夹具先保留非唯一索引再模拟唯一索引缺失。
            if (!sqlServer) await db.ExecuteAsync($"CREATE INDEX {support} ON fn_ai_knowledge_active_generation ({column})");
            await db.ExecuteAsync($"DROP INDEX {unique} ON fn_ai_knowledge_active_generation");
        }
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        Assert.AreEqual((Guid)p["Id"]!, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
        var comment = await db.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'ReceiptJson','ColumnId') AND name=N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='ReceiptJson'");
        Assert.AreEqual("无正文的完整有界批次回执", comment);
        p["Id"] = Guid.CreateVersion7();
        await Assert.ThrowsAsync<System.Data.Common.DbException>(async () => { await db.ExecuteAsync(insert, p); });
        var other = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        p["DocumentId"] = other["DocumentId"]; p["VersionId"] = other["VersionId"];
        await Assert.ThrowsAsync<System.Data.Common.DbException>(async () => { await db.ExecuteAsync(insert, p); });
    }

    internal static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("251_AiKnowledgeActivation.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
}
