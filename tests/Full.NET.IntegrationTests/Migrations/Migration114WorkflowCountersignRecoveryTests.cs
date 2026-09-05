using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 114 加签结构可从索引缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration114WorkflowCountersignRecoveryTests
{
    /// <summary>SQL Server 重跑 114 时必须补回加签项查询索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_countersign_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 114).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.tables WHERE name IN (N'fn_workflow_countersign_chain', N'fn_workflow_countersign_item')"));

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_countersign_item_Chain_Status ON dbo.fn_workflow_countersign_item;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%114_WorkflowCountersign.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 114).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_countersign_item') AND name = N'IX_fn_workflow_countersign_item_Chain_Status'"));
    }

    /// <summary>MySQL 重跑 114 时必须补回加签项查询索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_countersign_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 114).Successful);
        await using var connection = new MySqlConnection(
            Migration114To118RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('fn_workflow_countersign_chain', 'fn_workflow_countersign_item')"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_countersign_item DROP INDEX IX_fn_workflow_countersign_item_Chain_Status;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%114_WorkflowCountersign.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 114).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_countersign_item' AND INDEX_NAME = 'IX_fn_workflow_countersign_item_Chain_Status'"));
    }
}
