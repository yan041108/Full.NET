using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using DbUp.Helpers;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>策略根DDL已提交、其余运行表尚未完成时可安全恢复；不把构造根当实际运行证据。</summary>
[TestClass][DoNotParallelize]
public sealed class Migration349AiKnowledgeEvaluationRunsTests
{
    [TestMethod][DataRow(true)][DataRow(false)]
    public async Task Run_schema_and_observation_resume_partial_creation_without_losing_original_rows(bool sqlServer)
    {
        var connection=sqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=sqlServer?"SqlServer":"MySql",database=db.Database,migration=349})+"\n");
        var root=Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT")??throw new InvalidOperationException("必须指定已有源码根。");
        var directory=Path.Combine(root,"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",sqlServer?"SqlServer":"MySql");
        var script=await File.ReadAllTextAsync(Path.Combine(directory,"349_AiKnowledgeEvaluationRuns.sql"));
        var split=script.IndexOf(sqlServer?"IF OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_strategy_revision'":"CREATE TABLE IF NOT EXISTS fn_ai_knowledge_evaluation_strategy_revision",StringComparison.Ordinal);
        Assert.IsTrue(split>0);await Apply(script[..split]);var id=Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_evaluation_strategy (Id,KnowledgeBaseId,TenantId,CreatedById,Name,HeadRevisionId,HeadRevisionNo,Version,CreatedAtUtc) VALUES (@Id,@Knowledge,NULL,@Actor,'DDL恢复',@Revision,1,1,@Now)",
            new{Id=id,Knowledge=Guid.CreateVersion7(),Actor=Guid.CreateVersion7(),Revision=Guid.CreateVersion7(),Now=DateTime.UtcNow});
        await Apply(script);await Apply(script);Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_strategy WHERE Id=@Id AND Version=1",new{Id=id}));
        var observation=await File.ReadAllTextAsync(Path.Combine(directory,"350_AiKnowledgeAnswerObservations.sql"));await Apply(observation);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_answer_observation (Id,TenantId,KnowledgeBaseIdsJson,Refused,DurationMs,CreatedAtUtc) VALUES (@Id,NULL,'[]',0,NULL,@Now)",new{Id=id,Now=DateTime.UtcNow});
        await Apply(observation);Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_observation WHERE Id=@Id AND DurationMs IS NULL",new{Id=id}));
        async Task Apply(string sql)
        {
            if(!sqlServer){var result=DeployChanges.To.MySqlDatabase(connection).WithScript("evaluation-run-recovery",sql).JournalTo(new NullJournal()).Build().PerformUpgrade();if(!result.Successful)throw new InvalidOperationException("运行表恢复失败。",result.Error);return;}
            foreach(var batch in Regex.Split(sql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))if(!string.IsNullOrWhiteSpace(batch))await db.ExecuteAsync(batch);
        }
    }
}
