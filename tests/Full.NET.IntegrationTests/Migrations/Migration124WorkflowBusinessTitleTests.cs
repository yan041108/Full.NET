using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 124 业务标题列可从缺失状态恢复。</summary>
[TestClass]
public sealed class Migration124WorkflowBusinessTitleTests
{
    /// <summary>SQL Server 重跑 124 时必须补回业务标题相关列。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_business_title_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 124).Successful);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_workflow_definition DROP COLUMN BusinessTitleTemplate;
            ALTER TABLE dbo.fn_workflow_definition_version DROP COLUMN BusinessTitleTemplate;
            ALTER TABLE dbo.fn_workflow_instance DROP COLUMN BusinessTitle;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%124_WorkflowBusinessTitle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 124).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_definition') AND name = N'BusinessTitleTemplate'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_definition_version') AND name = N'BusinessTitleTemplate'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance') AND name = N'BusinessTitle'"));
    }

    /// <summary>MySQL 重跑 124 时必须补回业务标题相关列。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_business_title_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 124).Successful);
        await using var connection = new MySqlConnection(
            Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_definition DROP COLUMN BusinessTitleTemplate;
            ALTER TABLE fn_workflow_definition_version DROP COLUMN BusinessTitleTemplate;
            ALTER TABLE fn_workflow_instance DROP COLUMN BusinessTitle;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%124_WorkflowBusinessTitle.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 124).Successful);
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND COLUMN_NAME IN ('BusinessTitleTemplate', 'BusinessTitle')
              AND (
                  (TABLE_NAME = 'fn_workflow_definition' AND COLUMN_NAME = 'BusinessTitleTemplate')
                  OR (TABLE_NAME = 'fn_workflow_definition_version' AND COLUMN_NAME = 'BusinessTitleTemplate')
                  OR (TABLE_NAME = 'fn_workflow_instance' AND COLUMN_NAME = 'BusinessTitle')
              )
            """));
    }
}
