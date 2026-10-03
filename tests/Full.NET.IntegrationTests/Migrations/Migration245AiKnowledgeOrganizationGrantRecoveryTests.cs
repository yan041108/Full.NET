using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 245 授予迁移上界，半完成结构重放保留已有授予并收敛索引和注释。</summary>
[TestClass]
public sealed class Migration245AiKnowledgeOrganizationGrantRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_organization_grants_without_cross_module_foreign_keys() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_partial_organization_grants_without_cross_module_foreign_keys() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Replay(sqlServer, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var knowledge = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var parameters = new { Id = Guid.CreateVersion7(), Knowledge = knowledge, Document = document, Role = role, Owner = Guid.CreateVersion7(), Now = DateTime.UtcNow };
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_base (Id, OwnerUserId, Name, IsEnabled, DataClassification, CreatedAtUtc, Version) VALUES (@Knowledge,@Owner,'preserved',1,'internal',@Now,3)", parameters);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_document (Id,KnowledgeBaseId,Title,IsDeleted,CreatedAtUtc,Version) VALUES (@Document,@Knowledge,'preserved',0,@Now,4)", parameters);
        const string insertBase = "INSERT INTO fn_ai_knowledge_org (Id,KnowledgeBaseId,OrganizationUnitId,CreatedAtUtc) VALUES (@Id,@Knowledge,@Role,@Now)";
        await connection.ExecuteAsync(insertBase, parameters);
        // 模拟首表已提交但后续授予表尚未形成，NullJournal 确保不是零脚本重跑。
        await connection.ExecuteAsync("DROP TABLE fn_ai_knowledge_document_org");
        Replay(sqlServer, connectionString);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_org (Id,DocumentId,OrganizationUnitId,CreatedAtUtc) VALUES (@Id,@Document,@Role,@Now)", parameters);
        await connection.ExecuteAsync(sqlServer ? "DROP INDEX IX_fn_ai_knowledge_org_UnitBase ON dbo.fn_ai_knowledge_org" : "DROP INDEX IX_fn_ai_knowledge_org_UnitBase ON fn_ai_knowledge_org");
        if (sqlServer) await connection.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId'");
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_org", "IX_fn_ai_knowledge_org_UnitBase", sqlServer));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_org WHERE KnowledgeBaseId=@Knowledge AND OrganizationUnitId=@Role", parameters));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document_org WHERE DocumentId=@Document AND OrganizationUnitId=@Role", parameters));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>("SELECT Version FROM fn_ai_knowledge_base WHERE Id=@Knowledge", parameters));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Document", parameters));
        var rejected = false;
        try { await connection.ExecuteAsync(insertBase, new { Id = Guid.CreateVersion7(), Knowledge = knowledge, Role = role, Now = DateTime.UtcNow }); }
        catch (DbException) { rejected = true; }
        Assert.IsTrue(rejected);
    }
    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["240_AiKnowledgeBase.sql", "241_AiKnowledgeMember.sql", "242_AiKnowledgeDocument.sql", "245_AiKnowledgeOrganizationGrant.sql"];
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(4, result.Scripts);
    }
}
