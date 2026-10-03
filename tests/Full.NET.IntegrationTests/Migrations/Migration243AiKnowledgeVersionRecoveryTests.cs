using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>重放 240–243 修复缺失索引和注释，不覆盖不可变版本及恢复阶段。</summary>
[TestClass]
public sealed class Migration243AiKnowledgeVersionRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_immutable_versions_and_import_intents() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_immutable_versions_and_import_intents() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Replay(sqlServer, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var knowledge = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        var version = Guid.CreateVersion7();
        var requestKey = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, IsEnabled, DataClassification, CreatedAtUtc, Version) VALUES (@Id, NULL, @Actor, 'preserved', 1, 'internal', @Now, 1)", new { Id = knowledge, Actor = actor, Now = DateTime.UtcNow });
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_document (Id, KnowledgeBaseId, Title, IsDeleted, CreatedAtUtc, Version) VALUES (@Id, @Knowledge, 'preserved', 0, @Now, 2)", new { Id = document, Knowledge = knowledge, Now = DateTime.UtcNow });
        const string insertVersion = "INSERT INTO fn_ai_knowledge_version (Id, DocumentId, FileId, VersionNumber, OriginalFileName, ContentType, ContentHash, SizeBytes, UploadedByUserId, CreatedAtUtc) VALUES (@Id, @Document, @File, 1, 'source.txt', 'text/plain', @Hash, 4, @Actor, @Now)";
        const string insertImport = "INSERT INTO fn_ai_knowledge_import (Id, DocumentId, VersionId, RequestKey, ExpectedDocumentVersion, OriginalFileName, ContentType, ContentHash, SizeBytes, ActorUserId, StateKey, CreatedAtUtc, UpdatedAtUtc) VALUES (@Id, @Document, @Version, @RequestKey, 1, 'source.txt', 'text/plain', @Hash, 4, @Actor, 'pending_confirmation', @Now, @Now)";
        await connection.ExecuteAsync(insertVersion, new { Id = version, Document = document, File = Guid.CreateVersion7(), Actor = actor, Hash = new string('A', 64), Now = DateTime.UtcNow });
        await connection.ExecuteAsync(insertImport, new { Id = Guid.CreateVersion7(), Document = document, Version = version, RequestKey = requestKey, Actor = actor, Hash = new string('A', 64), Now = DateTime.UtcNow });
        await connection.ExecuteAsync(sqlServer ? "DROP INDEX IX_fn_ai_knowledge_import_DocState ON dbo.fn_ai_knowledge_import" : "DROP INDEX IX_fn_ai_knowledge_import_DocState ON fn_ai_knowledge_import");
        if (sqlServer) await connection.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_version', @level2type=N'COLUMN', @level2name=N'ContentHash'");
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_import", "IX_fn_ai_knowledge_import_DocState", sqlServer));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_version", "IX_fn_ai_knowledge_version_DocList", sqlServer));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_version WHERE Id=@Id AND VersionNumber=1 AND SizeBytes=4", new { Id = version }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_import WHERE VersionId=@Id AND StateKey='pending_confirmation'", new { Id = version }));
        // 重试键和版本序号由数据库兜底，重放不能降低这些唯一性约束。
        foreach (var statement in new[] { insertVersion, insertImport })
        {
            var rejected = false;
            try { await connection.ExecuteAsync(statement, new { Id = Guid.CreateVersion7(), Document = document, File = Guid.CreateVersion7(), Version = Guid.CreateVersion7(), RequestKey = requestKey, Actor = actor, Hash = new string('A', 64), Now = DateTime.UtcNow }); }
            catch (DbException) { rejected = true; }
            Assert.IsTrue(rejected);
        }
    }
    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["240_AiKnowledgeBase.sql", "241_AiKnowledgeMember.sql", "242_AiKnowledgeDocument.sql", "243_AiKnowledgeFileVersion.sql"];
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(4, result.Scripts);
    }
}
