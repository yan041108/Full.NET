using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 240–242，验证文档墓碑、授权、索引及 SQL Server 注释在未记账重放时保留。</summary>
[TestClass]
public sealed class Migration242AiKnowledgeDocumentRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_document_authority_without_reopening_deleted_documents() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_document_authority_without_reopening_deleted_documents() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Replay(sqlServer, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var knowledge = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, IsEnabled, DataClassification, CreatedAtUtc, Version)
            VALUES (@Id, NULL, @Owner, 'preserved', 1, 'internal', @CreatedAtUtc, 1)
            """, new { Id = knowledge, Owner = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow });
        var insertDocument = "INSERT INTO fn_ai_knowledge_document (Id, KnowledgeBaseId, Title, IsDeleted, CreatedAtUtc, Version) VALUES (@Id, @BaseId, 'preserved', 1, @CreatedAtUtc, 7)";
        await connection.ExecuteAsync(insertDocument, new { Id = document, BaseId = knowledge, CreatedAtUtc = DateTime.UtcNow });
        var insertMember = "INSERT INTO fn_ai_knowledge_document_member (Id, DocumentId, UserId, CreatedAtUtc) VALUES (@Id, @Document, @User, @CreatedAtUtc)";
        await connection.ExecuteAsync(insertMember, new { Id = Guid.CreateVersion7(), Document = document, User = user, CreatedAtUtc = DateTime.UtcNow });
        await connection.ExecuteAsync(sqlServer ? "DROP INDEX IX_fn_ai_knowledge_document_member_UserDoc ON dbo.fn_ai_knowledge_document_member"
            : "DROP INDEX IX_fn_ai_knowledge_document_member_UserDoc ON fn_ai_knowledge_document_member");
        if (sqlServer)
            await connection.ExecuteAsync("""
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document', @level2type=N'COLUMN', @level2name=N'Title'
                """);
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_document_member", "IX_fn_ai_knowledge_document_member_UserDoc", sqlServer));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_document", "IX_fn_ai_knowledge_document_BaseList", sqlServer));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document WHERE Id = @Id AND IsDeleted = 1 AND Version = 7", new { Id = document }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document_member WHERE DocumentId = @Document AND UserId = @User", new { Document = document, User = user }));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer ? """
            SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document')
                AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document'), N'Title', 'ColumnId') AND name = N'MS_Description'
            """ : """
            SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
                AND TABLE_NAME = 'fn_ai_knowledge_document' AND COLUMN_NAME = 'Title'
            """);
        Assert.AreEqual("受授权保护的文档标题", comment);
        // 约束只指向 AI 自有父记录；重复授权及悬空父记录不得进入权威状态。
        foreach (var parent in new[] { document, Guid.CreateVersion7() })
        {
            var rejected = false;
            try { await connection.ExecuteAsync(insertMember, new { Id = Guid.CreateVersion7(), Document = parent, User = user, CreatedAtUtc = DateTime.UtcNow }); }
            catch (DbException) { rejected = true; }
            Assert.IsTrue(rejected);
        }
        var orphanRejected = false;
        try { await connection.ExecuteAsync(insertDocument, new { Id = Guid.CreateVersion7(), BaseId = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow }); }
        catch (DbException) { orphanRejected = true; }
        Assert.IsTrue(orphanRejected);
    }

    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["240_AiKnowledgeBase.sql", "241_AiKnowledgeMember.sql", "242_AiKnowledgeDocument.sql"];
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(3, result.Scripts);
    }
}
