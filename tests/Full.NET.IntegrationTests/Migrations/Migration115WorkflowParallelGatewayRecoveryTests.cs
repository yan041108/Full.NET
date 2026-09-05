using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 115 并行网关结构可从索引缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration115WorkflowParallelGatewayRecoveryTests
{
    /// <summary>SQL Server 重跑 115 时必须补回步骤并行汇合索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_parallel_gateway_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 115).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step') AND name IN (N'ParallelJoinId', N'ParallelBranchKey')"));

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_step_ParallelJoin ON dbo.fn_workflow_step;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%115_WorkflowParallelGateway.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 115).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step') AND name = N'IX_fn_workflow_step_ParallelJoin'"));
    }

    /// <summary>MySQL 重跑 115 时必须补回步骤并行汇合索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_parallel_gateway_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 115).Successful);
        await using var connection = new MySqlConnection(
            Migration114To118RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME IN ('ParallelJoinId', 'ParallelBranchKey')"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_step DROP INDEX IX_fn_workflow_step_ParallelJoin;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%115_WorkflowParallelGateway.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 115).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND INDEX_NAME = 'IX_fn_workflow_step_ParallelJoin'"));
    }
}
