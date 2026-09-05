using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 122 发起人列表索引可从缺失状态恢复。</summary>
[TestClass]
public sealed class Migration122WorkflowInstanceInitiatorListIndexTests
{
    /// <summary>SQL Server 重跑 122 时必须补回发起人分页索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_initiator_list_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To122RecoveryTestSupport.MigrateSqlServer(connectionString, 122).Successful);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_instance_Scope_StartedBy ON dbo.fn_workflow_instance;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%122_WorkflowInstanceInitiatorListIndex.sql';
            """);

        Assert.IsTrue(Migration114To122RecoveryTestSupport.MigrateSqlServer(connectionString, 122).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance') AND name = N'IX_fn_workflow_instance_Scope_StartedBy'"));
    }

    /// <summary>MySQL 重跑 122 时必须补回发起人分页索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_initiator_list_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To122RecoveryTestSupport.MigrateMySql(connectionString, 122).Successful);
        await using var connection = new MySqlConnection(
            Migration114To122RecoveryTestSupport.MySqlQueryConnectionString(connectionString));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_instance DROP INDEX IX_fn_workflow_instance_Scope_StartedBy;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%122_WorkflowInstanceInitiatorListIndex.sql';
            """);

        Assert.IsTrue(Migration114To122RecoveryTestSupport.MigrateMySql(connectionString, 122).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_instance' AND INDEX_NAME = 'IX_fn_workflow_instance_Scope_StartedBy'"));
    }
}
