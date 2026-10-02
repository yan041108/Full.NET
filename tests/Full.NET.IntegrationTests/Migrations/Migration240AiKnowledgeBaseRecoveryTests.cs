using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结到精确 240 脚本，验证未记账的建表后恢复与审批字段约束。</summary>
[TestClass]
public sealed class Migration240AiKnowledgeBaseRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_created_catalog_without_index_and_comment() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_created_catalog_without_index() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Replay(sqlServer, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var id = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, CreatedAtUtc, Version)
            VALUES (@Id, NULL, @Owner, 'preserved', NULL, 1, 'internal', @CreatedAtUtc, 1)
            """, new { Id = id, Owner = owner, CreatedAtUtc = DateTime.UtcNow });
        // CREATE TABLE 是原子 DDL；模拟表已提交但后置索引及注释尚未提交，NullJournal 始终不记账。
        await connection.ExecuteAsync(sqlServer
            ? "DROP INDEX IX_fn_ai_knowledge_base_OwnerScope ON dbo.fn_ai_knowledge_base"
            : "DROP INDEX IX_fn_ai_knowledge_base_OwnerScope ON fn_ai_knowledge_base");
        if (sqlServer)
            await connection.ExecuteAsync("""
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'DataClassification'
                """);
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_base", "IX_fn_ai_knowledge_base_OwnerScope", sqlServer));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_ai_knowledge_base WHERE Id = @Id AND OwnerUserId = @Owner AND TenantId IS NULL
                AND Name = 'preserved' AND EmbeddingModelConfigId IS NULL AND EmbeddingModelVersion IS NULL
                AND GenerationModelConfigId IS NULL AND GenerationModelVersion IS NULL AND Version = 1
            """, new { Id = id, Owner = owner }));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer ? """
            SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
            WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base')
                AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'DataClassification', 'ColumnId') AND name = N'MS_Description'
            """ : """
            SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_base' AND COLUMN_NAME = 'DataClassification'
            """);
        Assert.AreEqual("数据分类机器码", comment);
        var rejected = false;
        try
        {
            await connection.ExecuteAsync("UPDATE fn_ai_knowledge_base SET EmbeddingModelConfigId = @ModelId WHERE Id = @Id", new { ModelId = Guid.CreateVersion7(), Id = id });
        }
        catch (DbException) { rejected = true; }
        Assert.IsTrue(rejected, "不完整的模型审批必须被双库约束拒绝。");
    }

    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("240_AiKnowledgeBase.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(1, result.Scripts);
    }
}
