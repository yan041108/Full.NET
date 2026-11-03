using System.Text.Json;
using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using DbUp.Helpers;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结到 346 的约束恢复：旧约束、约束已移除及已完成状态均保留原根并可重入。</summary>
[TestClass] [DoNotParallelize]
public sealed class Migration346AiKnowledgeFaqFeedbackTests
{
    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Feedback_origin_constraint_recovers_unjournaled_drop_without_losing_genesis(bool sqlServer)
    {
        var connection=sqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K04_FEEDBACK_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=sqlServer?"SqlServer":"MySql",database=db.Database,migration=346})+"\n");
        var source=Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT")??throw new InvalidOperationException("必须指定源码根。");
        var scripts=Path.Combine(source,"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",sqlServer?"SqlServer":"MySql");
        await Apply(await File.ReadAllTextAsync(Path.Combine(scripts,"343_AiKnowledgeFaqDrafts.sql")));
        var id=Guid.CreateVersion7();await Insert(id,"independent","原始独立依据");
        var upgrade=await File.ReadAllTextAsync(Path.Combine(scripts,"346_AiKnowledgeFaqFeedback.sql"));
        await Apply(upgrade);await Apply(upgrade);
        await db.ExecuteAsync(sqlServer?"ALTER TABLE fn_ai_knowledge_faq DROP CONSTRAINT CK_fn_ai_knowledge_faq_Bounds":"ALTER TABLE fn_ai_knowledge_faq DROP CHECK CK_fn_ai_knowledge_faq_Bounds");
        await Apply(upgrade);await Apply(upgrade);
        Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq WHERE Id=@Id AND OriginKey='independent' AND Basis=@Basis",new{Id=id,Basis="原始独立依据"}));
        await Insert(Guid.CreateVersion7(),"faq_derived",null);
        await Insert(Guid.CreateVersion7(),"feedback_derived",null);
        var error=await Assert.ThrowsAsync<DbException>(()=>Insert(Guid.CreateVersion7(),"unknown_derived",null));StringAssert.Contains(error.Message,"CK_fn_ai_knowledge_faq_Bounds");
        Assert.AreEqual(3,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq"));
        async Task Insert(Guid faq,string origin,string? basis){await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_faq (Id,KnowledgeBaseId,ReservedDocumentId,TenantId,CreatedById,CreateRequestKey,CreateHash,OriginKey,Basis,SourceJson,SourceHash,HeadRevisionId,HeadVersion,CreatedAtUtc) VALUES (@Id,@Knowledge,@Document,NULL,@Actor,@Request,@Hash,@Origin,@Basis,'{}',@Hash,@Revision,1,@Now)",
            new{Id=faq,Knowledge=Guid.CreateVersion7(),Document=Guid.CreateVersion7(),Actor=Guid.CreateVersion7(),Request=Guid.CreateVersion7(),Hash=new string('A',64),Origin=origin,Basis=basis,Revision=Guid.CreateVersion7(),Now=DateTime.UtcNow});}
        async Task Apply(string script)
        {
            if(!sqlServer)
            {
                var result=DeployChanges.To.MySqlDatabase(connection).WithScript("faq-feedback-recovery",script).JournalTo(new NullJournal()).Build().PerformUpgrade();
                if(!result.Successful)throw new InvalidOperationException("MySQL 反馈 FAQ 静态恢复过程执行失败。",result.Error);return;
            }
            foreach(var batch in Regex.Split(script,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))if(!string.IsNullOrWhiteSpace(batch))await db.ExecuteAsync(batch);
        }
    }
}
