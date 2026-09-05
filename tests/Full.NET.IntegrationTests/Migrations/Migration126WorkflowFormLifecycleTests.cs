using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 126 表单生命周期列可从缺失状态恢复。</summary>
[TestClass]
public sealed class Migration126WorkflowFormLifecycleTests
{
    /// <summary>SQL Server 重跑 126 时必须补回 StatusKey 与 Version 列。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_form_lifecycle_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 126).Successful);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_workflow_form_definition DROP CONSTRAINT CK_fn_workflow_form_definition_StatusKey;
            ALTER TABLE dbo.fn_workflow_form_definition DROP CONSTRAINT CK_fn_workflow_form_definition_Version;
            ALTER TABLE dbo.fn_workflow_form_definition DROP CONSTRAINT DF_fn_workflow_form_definition_StatusKey;
            ALTER TABLE dbo.fn_workflow_form_definition DROP CONSTRAINT DF_fn_workflow_form_definition_Version;
            ALTER TABLE dbo.fn_workflow_form_definition DROP COLUMN StatusKey;
            ALTER TABLE dbo.fn_workflow_form_definition DROP COLUMN Version;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%126_WorkflowFormLifecycle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 126).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_form_definition') AND name = N'StatusKey'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_form_definition') AND name = N'Version'"));
    }

    /// <summary>MySQL 重跑 126 时必须补回 StatusKey 与 Version 列。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_form_lifecycle_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 126).Successful);
        await using var connection = new MySqlConnection(
            Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_form_definition DROP CHECK CK_fn_workflow_form_definition_StatusKey;
            ALTER TABLE fn_workflow_form_definition DROP CHECK CK_fn_workflow_form_definition_Version;
            ALTER TABLE fn_workflow_form_definition DROP COLUMN StatusKey;
            ALTER TABLE fn_workflow_form_definition DROP COLUMN Version;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%126_WorkflowFormLifecycle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 126).Successful);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_workflow_form_definition'
              AND COLUMN_NAME IN ('StatusKey', 'Version')
            """));
    }
}
