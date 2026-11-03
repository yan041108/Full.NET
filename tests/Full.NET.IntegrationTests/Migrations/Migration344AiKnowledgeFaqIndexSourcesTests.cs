using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DbUp;
using DbUp.Helpers;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>FAQ 来源 DDL 在表已提交、列及约束尚未完成时恢复；保留旧文件版本，不制造文件占位值。</summary>
[TestClass] [DoNotParallelize]
public sealed class Migration344AiKnowledgeFaqIndexSourcesTests
{
    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Faq_index_source_schema_recovers_partial_ddl_and_enforces_source_exclusivity(bool sqlServer)
    {
        var connection=sqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K04_LIFECYCLE_DATABASE_REGISTRY") is{Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=sqlServer?"SqlServer":"MySql",database=db.Database,migration=344})+"\n");
        await db.ExecuteAsync(sqlServer?"CREATE TABLE fn_ai_knowledge_version (Id uniqueidentifier NOT NULL,FileId uniqueidentifier NOT NULL); CREATE TABLE fn_ai_knowledge_faq (Id uniqueidentifier NOT NULL);"
            :"CREATE TABLE fn_ai_knowledge_version (Id BINARY(16) NOT NULL,FileId BINARY(16) NOT NULL); CREATE TABLE fn_ai_knowledge_faq (Id BINARY(16) NOT NULL);");
        var old=Guid.CreateVersion7();var file=Guid.CreateVersion7();await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,FileId) VALUES (@Id,@File)",new{Id=old,File=file});
        var root=Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT")??throw new InvalidOperationException("必须绑定真实源码根。");
        var script=await File.ReadAllTextAsync(Path.Combine(root,"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",sqlServer?"SqlServer":"MySql","344_AiKnowledgeFaqIndexSources.sql"));
        var marker=sqlServer?"IF OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'":"CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_release";
        var split=script.IndexOf(marker,StringComparison.Ordinal);Assert.IsTrue(split>0);await Apply(script[..split]);await Apply(script);
        var claim=Guid.CreateVersion7();var knowledge=Guid.CreateVersion7();var faq=Guid.CreateVersion7();var hash=new string('a',64);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_faq_query (Id,KnowledgeBaseId,FaqId,QueryHash,CreatedAtUtc) VALUES (@Id,@Knowledge,@Faq,@Hash,@Now)",new{Id=claim,Knowledge=knowledge,Faq=faq,Hash=hash,Now=DateTime.UtcNow});
        // 模拟隐式提交后的局部丢失；重跑必须恢复结构，同时保留既有发布占用和文件身份。
        await db.ExecuteAsync(sqlServer?"DROP INDEX UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash ON fn_ai_knowledge_faq_query; ALTER TABLE fn_ai_knowledge_version DROP CONSTRAINT CK_fn_ai_knowledge_version_SourceKind;"
            :"ALTER TABLE fn_ai_knowledge_faq_query DROP INDEX UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash; ALTER TABLE fn_ai_knowledge_version DROP CHECK CK_fn_ai_knowledge_version_SourceKind;");
        await Apply(script);await Apply(script);
        Assert.AreEqual(file,await db.QuerySingleAsync<Guid>("SELECT FileId FROM fn_ai_knowledge_version WHERE Id=@Id",new{Id=old}));
        Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq_query WHERE Id=@Id AND QueryHash=@Hash",new{Id=claim,Hash=hash}));
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,FileId,FaqRevisionId) VALUES (@Id,NULL,@Revision)",new{Id=Guid.CreateVersion7(),Revision=Guid.CreateVersion7()});
        await Assert.ThrowsAsync<System.Data.Common.DbException>(()=>db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,FileId,FaqRevisionId) VALUES (@Id,NULL,NULL)",new{Id=Guid.CreateVersion7()}));
        await Assert.ThrowsAsync<System.Data.Common.DbException>(()=>db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,FileId,FaqRevisionId) VALUES (@Id,@File,@Revision)",new{Id=Guid.CreateVersion7(),File=file,Revision=Guid.CreateVersion7()}));
        await Assert.ThrowsAsync<System.Data.Common.DbException>(()=>db.ExecuteAsync("INSERT INTO fn_ai_knowledge_faq_query (Id,KnowledgeBaseId,FaqId,QueryHash,CreatedAtUtc) VALUES (@Id,@Knowledge,@Faq,@Hash,@Now)",new{Id=Guid.CreateVersion7(),Knowledge=knowledge,Faq=Guid.CreateVersion7(),Hash=hash,Now=DateTime.UtcNow}));
        Assert.AreEqual(3,await db.QuerySingleAsync<int>(sqlServer?"SELECT COUNT(*) FROM sys.tables WHERE name IN ('fn_ai_knowledge_faq_source','fn_ai_knowledge_faq_release','fn_ai_knowledge_faq_query')"
            :"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('fn_ai_knowledge_faq_source','fn_ai_knowledge_faq_release','fn_ai_knowledge_faq_query')"));
        async Task Apply(string text)
        {
            if(!sqlServer)
            {
                var result=DeployChanges.To.MySqlDatabase(connection).WithScript("faq-source-recovery",text).JournalTo(new NullJournal()).Build().PerformUpgrade();
                if(!result.Successful)throw new InvalidOperationException("MySQL 静态恢复过程执行失败。",result.Error);return;
            }
            foreach(var batch in Regex.Split(text,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))if(!string.IsNullOrWhiteSpace(batch))await db.ExecuteAsync(batch);
        }
    }
}
