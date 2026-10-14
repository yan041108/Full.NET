using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>新增可空上下文可从旧结构和说明未完成状态恢复，重放不得改写已有追踪或运行版本。</summary>
[TestClass]
public sealed class Migration259AiAgentRunTraceContextRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_nullable_trace_context_without_rewriting_runs() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_nullable_trace_context_without_rewriting_runs() => VerifyAsync(false);
    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay(["212_AiAgentRuntime.sql"]);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = new { Id = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7(),
            ClientId = Guid.CreateVersion7(), BindingId = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow,
            Deadline = DateTimeOffset.UtcNow.AddMinutes(5) };
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_agent_run (Id,ScopeKey,ActorUserId,SessionId,ClientRequestId,RequestHash,DefinitionKey,
                DefinitionVersion,AuthorizationBindingId,SecurityStamp,ActorScope,EffectiveScope,StatusKey,BudgetJson,
                DeadlineAtUtc,Version,LeaseEpoch,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@Id,'host',@UserId,@SessionId,@ClientId,'hash','single',1,@BindingId,'stamp','host','host','queued','{}',@Deadline,7,2,@Now,@Now)
            """, p);
        Replay(["259_AiAgentRunTraceContext.sql"]);
        Assert.IsNull(await db.QuerySingleAsync<string?>("SELECT TraceParent FROM fn_ai_agent_run WHERE Id=@Id", p));
        const string origin = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_agent_run SET TraceParent=@Origin WHERE Id=@Id", new { p.Id, Origin = origin }));
        // 模拟 DDL 已提交而列说明未完成；通过 NullJournal 原样重跑，避免只验证已记账的零脚本路径。
        await db.ExecuteAsync(sqlServer
            ? "EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_agent_run',@level2type=N'COLUMN',@level2name=N'TraceParent'"
            : "ALTER TABLE fn_ai_agent_run MODIFY COLUMN TraceParent varchar(55) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''");
        Replay(["259_AiAgentRunTraceContext.sql"]); Replay(["259_AiAgentRunTraceContext.sql"]);
        Assert.AreEqual(origin, await db.QuerySingleAsync<string>("SELECT TraceParent FROM fn_ai_agent_run WHERE Id=@Id", p));
        Assert.AreEqual(7L, await db.QuerySingleAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", p));
        Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT LeaseEpoch FROM fn_ai_agent_run WHERE Id=@Id", p));
        Assert.AreEqual("运行创建时的 W3C 上下文；仅作恢复追踪链接", await db.QuerySingleAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_agent_run') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_agent_run'),'TraceParent','ColumnId') AND name='MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_agent_run' AND COLUMN_NAME='TraceParent'"));
        void Replay(string[] names)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && names.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(names.Length, result.Scripts);
        }
    }
}
