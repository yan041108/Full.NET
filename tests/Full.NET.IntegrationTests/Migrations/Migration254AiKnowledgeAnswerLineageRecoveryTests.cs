using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Engine;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>DDL 恢复、双库实际原子更新和并发不能由替身证明；使用现有隔离数据库夹具。</summary>
[TestClass]
public sealed class Migration254AiKnowledgeAnswerLineageRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_columns_comments_and_guard_without_losing_history() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_columns_comments_and_guard_without_losing_history() => RecoveryAsync(false);
    [TestMethod] public Task SqlServer_answer_writes_require_live_owned_lease_and_atomic_lineage() => WritesAsync(true);
    [TestMethod] public Task MySql_answer_writes_require_live_owned_lease_and_atomic_lineage() => WritesAsync(false);
    private const string Guard = "CK_fn_ai_chat_message_KnowledgeLineage";
    private const string InsertMessage = "INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc) VALUES (@MessageId,@SessionId,'assistant',@LegacyContent,'completed',@Now)";

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = await SessionAsync(db, false);
        Assert.AreEqual(1, await db.ExecuteAsync(InsertMessage, p));
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.fn_ai_chat_message DROP CONSTRAINT {Guard}" : $"ALTER TABLE fn_ai_chat_message DROP CHECK {Guard}");
        if (sqlServer) await db.ExecuteAsync("ALTER TABLE dbo.fn_ai_chat_message DROP CONSTRAINT DF_fn_ai_chat_message_IsKnowledgeAnswer");
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_ai_chat_message DROP COLUMN IsKnowledgeAnswer,KnowledgeLineage"
            : "ALTER TABLE fn_ai_chat_message DROP COLUMN IsKnowledgeAnswer,DROP COLUMN KnowledgeLineage");
        Replay(sqlServer, connectionString);
        Assert.AreEqual("普通聊天历史", await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT IsKnowledgeAnswer FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        // 模拟只完成首列或注释丢失；每条 DDL 重放均不得覆盖已有依赖或正文。
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.fn_ai_chat_message DROP CONSTRAINT {Guard}" : $"ALTER TABLE fn_ai_chat_message DROP CHECK {Guard}");
        await db.ExecuteAsync("ALTER TABLE fn_ai_chat_message DROP COLUMN KnowledgeLineage"); Replay(sqlServer, connectionString);
        await db.ExecuteAsync("UPDATE fn_ai_chat_message SET IsKnowledgeAnswer=1,KnowledgeLineage='{}' WHERE Id=@MessageId", p);
        foreach (var column in new[] { "IsKnowledgeAnswer", "KnowledgeLineage" })
        {
            await db.ExecuteAsync(sqlServer
                ? $"EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_chat_message',@level2type=N'COLUMN',@level2name=N'{column}'"
                : column == "IsKnowledgeAnswer" ? "ALTER TABLE fn_ai_chat_message MODIFY COLUMN IsKnowledgeAnswer boolean NOT NULL DEFAULT 0 COMMENT ''"
                    : "ALTER TABLE fn_ai_chat_message MODIFY COLUMN KnowledgeLineage longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT ''");
            Replay(sqlServer, connectionString);
            var comment = await db.ExecuteScalarAsync<string>(sqlServer
                ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_message') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'),@Column,'ColumnId') AND name=N'MS_Description'"
                : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_chat_message' AND COLUMN_NAME=@Column", new { Column = column });
            Assert.AreEqual(column == "IsKnowledgeAnswer" ? "是否携带知识来源依赖" : "绑定答案消息及可信范围的版本化完整来源依赖", comment);
        }
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.fn_ai_chat_message NOCHECK CONSTRAINT {Guard}" : $"ALTER TABLE fn_ai_chat_message ALTER CHECK {Guard} NOT ENFORCED");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        Assert.AreEqual("{}", await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        Assert.AreEqual("普通聊天历史", await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("UPDATE fn_ai_chat_message SET IsKnowledgeAnswer=0 WHERE Id=@MessageId", p); });
        p["KnowledgeLineage"] = new string('A', 16385);
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@KnowledgeLineage WHERE Id=@MessageId", p); });
        Assert.AreEqual("{}", await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@MessageId", p));
    }
    private static async Task WritesAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        await using var other = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        foreach (var tenant in new[] { false, true })
        {
            var p = await SessionAsync(db, tenant); var insert = AiKnowledgeAnswerSql.InsertFor(tenant).Text; var complete = AiKnowledgeAnswerSql.CompleteFor(tenant).Text;
            Assert.AreEqual(0, await db.ExecuteAsync(AiKnowledgeAnswerSql.InsertFor(!tenant).Text, p));
            Assert.AreEqual(0, await db.ExecuteAsync(insert, Copy(p, "OwnerUserId", Guid.CreateVersion7())));
            Assert.AreEqual(1, await db.ExecuteAsync(insert, p));
            foreach (var fault in new[] { "owner", "tenant", "generation", "expired", "cancelled", "session" })
            {
                var input = Copy(p, "OwnerUserId", p["OwnerUserId"]);
                switch (fault)
                {
                    case "owner": input["OwnerUserId"] = Guid.CreateVersion7(); break;
                    case "tenant": input["TenantId"] = Guid.CreateVersion7(); break;
                    case "session": input["SessionId"] = Guid.CreateVersion7(); break;
                    case "generation": await db.ExecuteAsync("UPDATE fn_ai_chat_session SET GenerationId=@Wrong WHERE Id=@SessionId", new { SessionId = p["SessionId"], Wrong = Guid.CreateVersion7() }); break;
                    case "expired": input["Now"] = p["ExpiresAtUtc"]; break;
                    case "cancelled": await db.ExecuteAsync("UPDATE fn_ai_chat_session SET GenerationCancellationRequested=1 WHERE Id=@SessionId", p); break;
                }
                // Host 不绑定 TenantId 参数；错误租户由 Tenant 语句或范围切换条件证明。
                var sql = fault == "tenant" && !tenant ? AiKnowledgeAnswerSql.CompleteFor(true).Text : complete;
                Assert.AreEqual(0, await db.ExecuteAsync(sql, input), fault);
                await db.ExecuteAsync("UPDATE fn_ai_chat_session SET GenerationId=@MessageId,GenerationCancellationRequested=0 WHERE Id=@SessionId", p);
            }
            var large = Copy(p, "KnowledgeLineage", new string('A', 16385));
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(complete, large); });
            Assert.AreEqual("", await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", p));
            var results = await Task.WhenAll(db.ExecuteAsync(complete, p), other.ExecuteAsync(complete, p));
            Assert.AreEqual(1, results.Sum()); Assert.AreEqual(0, await db.ExecuteAsync(complete, p));
            Assert.AreEqual("知识答案", await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", p));
            Assert.AreEqual("{}", await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@MessageId", p));
        }
    }
    private static Dictionary<string, object?> Copy(Dictionary<string, object?> p, string key, object? value) => new(p) { [key] = value };
    private static async Task<Dictionary<string, object?>> SessionAsync(DbConnection db, bool tenant)
    {
        var now = DateTime.UtcNow; now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var p = new Dictionary<string, object?> { ["SessionId"] = Guid.CreateVersion7(), ["MessageId"] = Guid.CreateVersion7(),
            ["OwnerUserId"] = Guid.CreateVersion7(), ["TenantId"] = tenant ? Guid.CreateVersion7() : null,
            ["ModelConfigId"] = Guid.CreateVersion7(), ["Now"] = now, ["ExpiresAtUtc"] = now.AddSeconds(30), ["Content"] = "知识答案", ["LegacyContent"] = "普通聊天历史", ["KnowledgeLineage"] = "{}" };
        await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,IsGenerating,GenerationId,GenerationExpiresAtUtc,GenerationCancellationRequested,CreatedAtUtc) VALUES (@SessionId,@TenantId,@OwnerUserId,@ModelConfigId,'model','session',1,@MessageId,@ExpiresAtUtc,0,@Now)", p);
        return p;
    }
    private static async Task<string> DatabaseAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        // 冻结实际需要的历史脚本集合；255 及后续脚本不能静默进入 254 恢复用例。
        string[] scripts = ["184_AiModelConfig.sql", "186_AiChatSession.sql", "203_PublishedModuleUuidStorage.sql", "205_AiChatGenerationLease.sql", "254_AiKnowledgeAnswerLineage.sql"];
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(scripts, StringComparer.Ordinal)))
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(scripts.Length, result.Scripts);
        return connectionString;
    }
    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
            name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("254_AiKnowledgeAnswerLineage.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
}
