using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 123 已办历史索引可从缺失状态恢复。</summary>
[TestClass]
public sealed class Migration123WorkflowTodoHistoryListIndexTests
{
    /// <summary>SQL Server 重跑 123 时必须补回已办历史索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_history_list_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 123).Successful);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_todo_Assignee_Completed ON dbo.fn_workflow_todo;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%123_WorkflowTodoHistoryListIndex.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 123).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'IX_fn_workflow_todo_Assignee_Completed'"));
    }

    /// <summary>MySQL 重跑 123 时必须补回已办历史索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_history_list_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 123).Successful);
        await using var connection = new MySqlConnection(
            Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_todo DROP INDEX IX_fn_workflow_todo_Assignee_Completed;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%123_WorkflowTodoHistoryListIndex.sql';
            """);

        Assert.IsTrue(Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 123).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND INDEX_NAME = 'IX_fn_workflow_todo_Assignee_Completed'"));
    }
}
