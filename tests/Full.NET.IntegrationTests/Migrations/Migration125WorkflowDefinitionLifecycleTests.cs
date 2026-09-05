using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 125 定义生命周期状态列可从缺失状态恢复。</summary>
[TestClass]
public sealed class Migration125WorkflowDefinitionLifecycleTests
{
    /// <summary>SQL Server 重跑 125 时必须补回 StatusKey 列与检查约束。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_definition_status_column()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 125).Successful);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_workflow_definition DROP CONSTRAINT CK_fn_workflow_definition_StatusKey;
            ALTER TABLE dbo.fn_workflow_definition DROP CONSTRAINT DF_fn_workflow_definition_StatusKey;
            ALTER TABLE dbo.fn_workflow_definition DROP COLUMN StatusKey;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%125_WorkflowDefinitionLifecycle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 125).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_definition') AND name = N'StatusKey'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_definition') AND name = N'CK_fn_workflow_definition_StatusKey'"));
    }

    /// <summary>MySQL 重跑 125 时必须补回 StatusKey 列与检查约束。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_definition_status_column()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 125).Successful);
        await using var connection = new MySqlConnection(
            Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_definition DROP CHECK CK_fn_workflow_definition_StatusKey;
            ALTER TABLE fn_workflow_definition DROP COLUMN StatusKey;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%125_WorkflowDefinitionLifecycle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 125).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_workflow_definition'
              AND COLUMN_NAME = 'StatusKey'
            """));
    }
}
