using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 116 包容网关结构可从约束缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration116WorkflowInclusiveGatewayRecoveryTests
{
    /// <summary>SQL Server 重跑 116 时必须补回网关类型约束。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_inclusive_gateway_constraint()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 116).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name = N'GatewayTypeKey'"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_workflow_parallel_join DROP CONSTRAINT CK_fn_workflow_parallel_join_GatewayTypeKey;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%116_WorkflowInclusiveGateway.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 116).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name = N'CK_fn_workflow_parallel_join_GatewayTypeKey'"));
    }

    /// <summary>MySQL 重跑 116 时必须补回网关类型约束。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_inclusive_gateway_constraint()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 116).Successful);
        await using var connection = new MySqlConnection(
            Migration114To118RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_parallel_join' AND COLUMN_NAME = 'GatewayTypeKey'"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_parallel_join DROP CHECK CK_fn_workflow_parallel_join_GatewayTypeKey;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%116_WorkflowInclusiveGateway.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 116).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_parallel_join' AND CONSTRAINT_NAME = 'CK_fn_workflow_parallel_join_GatewayTypeKey'"));
    }
}
