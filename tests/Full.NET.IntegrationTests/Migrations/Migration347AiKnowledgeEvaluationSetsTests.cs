using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using DbUp.Helpers;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>只执行准确的347脚本，复原根表已提交但其余表和记账尚未完成的部署中断。</summary>
[TestClass] [DoNotParallelize]
public sealed class Migration347AiKnowledgeEvaluationSetsTests
{
    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Evaluation_tables_resume_unjournaled_partial_creation_without_removing_existing_roots(bool sqlServer)
    {
        var connection=sqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=sqlServer?"SqlServer":"MySql",database=db.Database,migration=347})+"\n");
        var root=Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT")??throw new InvalidOperationException("必须指定已有源码根。");
        var file=Path.Combine(root,"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",sqlServer?"SqlServer":"MySql","347_AiKnowledgeEvaluationSets.sql");
        var script=await File.ReadAllTextAsync(file);
        var split=script.IndexOf(sqlServer?"IF OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'":"CREATE TABLE IF NOT EXISTS fn_ai_knowledge_evaluation_revision",StringComparison.Ordinal);
        Assert.IsTrue(split>0);await Apply(script[..split]);
        var id=Guid.CreateVersion7();
        // 本行只验证DDL恢复保留数据，不作为应用规范正文或有效来源的证明。
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_evaluation_set (Id,KnowledgeBaseId,TenantId,CreatedById,RequestKey,CreateHash,HeadRevisionId,HeadRevisionNo,Version,FootprintJson,FootprintHash,CreatedAtUtc) VALUES (@Id,@Knowledge,NULL,@Actor,@Request,@Hash,@Revision,1,1,'{}',@Hash,@Now)",
            new{Id=id,Knowledge=Guid.CreateVersion7(),Actor=Guid.CreateVersion7(),Request=Guid.CreateVersion7(),Hash=new string('A',64),Revision=Guid.CreateVersion7(),Now=DateTime.UtcNow});
        await Apply(script);await Apply(script);
        Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_set WHERE Id=@Id AND Version=1",new{Id=id}));
        var count=sqlServer?await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name IN ('fn_ai_knowledge_evaluation_set','fn_ai_knowledge_evaluation_revision','fn_ai_knowledge_evaluation_approval')")
            :await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('fn_ai_knowledge_evaluation_set','fn_ai_knowledge_evaluation_revision','fn_ai_knowledge_evaluation_approval')");
        Assert.AreEqual(3,count);
        async Task Apply(string sql)
        {
            if(!sqlServer)
            {
                var result=DeployChanges.To.MySqlDatabase(connection).WithScript("evaluation-347-recovery",sql).JournalTo(new NullJournal()).Build().PerformUpgrade();
                if(!result.Successful)throw new InvalidOperationException("MySQL评估表恢复失败。",result.Error);return;
            }
            foreach(var batch in Regex.Split(sql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
                if(!string.IsNullOrWhiteSpace(batch))await db.ExecuteAsync(batch);
        }
    }
}
