using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>成对 DDL 在第一张表已提交、迁移尚未记账时恢复；不重建已有草稿根或更改其来源。</summary>
[TestClass] [DoNotParallelize]
public sealed class Migration343AiKnowledgeFaqDraftTests
{
    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Faq_draft_schema_recovers_partial_creation_and_preserves_existing_genesis(bool sqlServer)
    {
        var connection = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connection); await db.OpenAsync();
        if (Environment.GetEnvironmentVariable("FULLNET_K04_DRAFT_DATABASE_REGISTRY") is { Length: > 0 } registry)
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = sqlServer ? "SqlServer" : "MySql", database = db.Database, migration = 343 }) + "\n");
        var root = Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("限定验收必须提供源码根。");
        var script = await File.ReadAllTextAsync(Path.Combine(root, "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations", sqlServer ? "SqlServer" : "MySql", "343_AiKnowledgeFaqDrafts.sql"));
        var marker = sqlServer ? "IF OBJECT_ID(N'dbo.fn_ai_knowledge_faq_revision'" : "CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_revision";
        var split = script.IndexOf(marker, StringComparison.Ordinal); Assert.IsTrue(split > 0);
        await Apply(script[..split]); var id = Guid.CreateVersion7(); var hash = new string('A',64);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_faq (Id,KnowledgeBaseId,ReservedDocumentId,TenantId,CreatedById,CreateRequestKey,CreateHash,OriginKey,Basis,SourceJson,SourceHash,HeadRevisionId,HeadVersion,CreatedAtUtc) VALUES (@Id,@Knowledge,@Document,NULL,@Actor,@Request,@Hash,'independent','独立编写依据','{}',@Hash,@Revision,1,@Now)",
            new { Id=id,Knowledge=Guid.CreateVersion7(),Document=Guid.CreateVersion7(),Actor=Guid.CreateVersion7(),Request=Guid.CreateVersion7(),Hash=hash,Revision=Guid.CreateVersion7(),Now=DateTime.UtcNow });
        await Apply(script); await Apply(script);
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq WHERE Id=@Id AND SourceHash=@Hash AND HeadVersion=1",new {Id=id,Hash=hash}));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq_revision"));
        Assert.AreEqual(2, await db.QuerySingleAsync<int>(sqlServer ? "SELECT COUNT(*) FROM sys.tables WHERE name IN ('fn_ai_knowledge_faq','fn_ai_knowledge_faq_revision')" : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('fn_ai_knowledge_faq','fn_ai_knowledge_faq_revision')"));
        Assert.AreEqual(4, await db.QuerySingleAsync<int>(sqlServer ? "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('UX_fn_ai_knowledge_faq_ReservedDocumentId','UX_fn_ai_knowledge_faq_KnowledgeBaseId_CreateRequestKey','UX_fn_ai_knowledge_faq_revision_FaqId_RequestKey','UX_fn_ai_knowledge_faq_revision_FaqId_RevisionNo')" : "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=DATABASE() AND index_name IN ('UX_fn_ai_knowledge_faq_ReservedDocumentId','UX_fn_ai_knowledge_faq_KnowledgeBaseId_CreateRequestKey','UX_fn_ai_knowledge_faq_revision_FaqId_RequestKey','UX_fn_ai_knowledge_faq_revision_FaqId_RevisionNo')"));
        async Task Apply(string text)
        {
            foreach (var batch in sqlServer ? Regex.Split(text, @"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase) : new[] {text})
                if (!string.IsNullOrWhiteSpace(batch)) await db.ExecuteAsync(batch);
        }
    }
}
