using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using DbUp.Helpers;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>迁移恢复仅核证结构与原操作约束，不把构造回执行当作业务执行证据。</summary>
[TestClass][DoNotParallelize]
public sealed class Migration351PersonalScheduleCompletionTests
{
    [TestMethod][DataRow(true)][DataRow(false)]
    public async Task Completion_schema_restores_partial_ddl_and_original_operation_constraints(bool sqlServer)
    {
        var connection=sqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K06_COMPLETION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=sqlServer?"SqlServer":"MySql",database=db.Database,migration=351})+"\n");
        var root=Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT")??throw new InvalidOperationException("必须复用已有源码根。");
        var script=await File.ReadAllTextAsync(Path.Combine(root,"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",sqlServer?"SqlServer":"MySql","351_PersonalScheduleCompletion.sql"));
        var split=script.IndexOf(sqlServer?"IF OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'":"CREATE TABLE IF NOT EXISTS fn_ai_schedule_completion_intent",StringComparison.Ordinal);
        Assert.IsTrue(split>0);await Apply(script[..split]);
        var operation=Guid.CreateVersion7();
        async Task Insert(string outcome,int? version,DateTimeOffset? completed)=>_ = await db.ExecuteAsync("INSERT INTO fn_calendar_schedule_completion (Id,TenantId,ScopeKey,OwnerUserId,RunId,OperationId,ScheduleId,ExpectedVersion,RequestDigest,Outcome,Reason,ActualVersion,CompletedAtUtc,CreatedAtUtc) VALUES (@Id,NULL,'host',@Actor,@Run,@Operation,@Schedule,1,@Hash,@Outcome,@Reason,@Version,@Completed,@Now)",
            new{Id=Guid.CreateVersion7(),Actor=Guid.CreateVersion7(),Run=Guid.CreateVersion7(),Operation=operation,Schedule=Guid.CreateVersion7(),Hash=new string('A',64),Outcome=outcome,Reason=outcome=="rejected"?"target_changed":null,Version=version,Completed=completed,Now=DateTimeOffset.UtcNow});
        await Insert("rejected",null,null);await Apply(script);
        await db.ExecuteAsync(sqlServer?"ALTER TABLE fn_calendar_schedule_completion DROP CONSTRAINT UX_fn_calendar_schedule_completion_Operation":"ALTER TABLE fn_calendar_schedule_completion DROP INDEX UX_fn_calendar_schedule_completion_Operation");
        await db.ExecuteAsync(sqlServer?"ALTER TABLE fn_ai_schedule_completion_intent DROP CONSTRAINT CK_fn_ai_schedule_completion_intent_Bounds":"ALTER TABLE fn_ai_schedule_completion_intent DROP CHECK CK_fn_ai_schedule_completion_intent_Bounds");
        await db.ExecuteAsync(sqlServer?"ALTER TABLE fn_ai_schedule_completion_intent DROP CONSTRAINT UX_fn_ai_schedule_completion_intent_Target":"ALTER TABLE fn_ai_schedule_completion_intent DROP INDEX UX_fn_ai_schedule_completion_intent_Target");
        await Apply(script);await Apply(script);
        Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_calendar_schedule_completion WHERE OperationId=@Operation",new{Operation=operation}));
        await Assert.ThrowsAsync<DbException>(()=>Insert("rejected",null,null));
        await Assert.ThrowsAsync<DbException>(()=>db.ExecuteAsync("UPDATE fn_calendar_schedule_completion SET Reason=NULL WHERE OperationId=@Operation",new{Operation=operation}));
        operation=Guid.CreateVersion7();await Assert.ThrowsAsync<DbException>(()=>Insert("completed",1,DateTimeOffset.UtcNow));
        operation=Guid.CreateVersion7();await Assert.ThrowsAsync<DbException>(()=>Insert("completed",null,DateTimeOffset.UtcNow));
        // unknown保留准确目标版本的唯一占用，不能用另一OperationId绕过原操作对账。
        var scheduleId=Guid.CreateVersion7();
        async Task InsertIntent()=>_ = await db.ExecuteAsync("INSERT INTO fn_ai_schedule_completion_intent (Id,TenantId,ScopeKey,OwnerUserId,RunId,OperationId,ScheduleId,ExpectedVersion,RequestJson,RequestDigest,Outcome,ReceiptJson,Version,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,NULL,'host',@Owner,@Run,@Operation,@Schedule,1,'{}',@Hash,'unknown',NULL,1,@Now,@Now)",
            new{Id=Guid.CreateVersion7(),Owner=Guid.CreateVersion7(),Run=Guid.CreateVersion7(),Operation=Guid.CreateVersion7(),Schedule=scheduleId,Hash=new string('A',64),Now=DateTimeOffset.UtcNow});
        await InsertIntent(); await Assert.ThrowsAsync<DbException>(InsertIntent);
        async Task Apply(string sql)
        {
            if(!sqlServer){var result=DeployChanges.To.MySqlDatabase(connection).WithScript("completion-recovery",sql).JournalTo(new NullJournal()).Build().PerformUpgrade();if(!result.Successful)throw new InvalidOperationException("完成操作结构恢复失败。",result.Error);return;}
            foreach(var batch in Regex.Split(sql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))if(!string.IsNullOrWhiteSpace(batch))await db.ExecuteAsync(batch);
        }
    }
}
