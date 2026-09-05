using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 121 数据审批请求应用元数据可从索引缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration121DataApprovalRequestApplicationTests
{
    /// <summary>SQL Server 重跑 121 时必须补回待应用扫描索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_data_approval_application_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To121RecoveryTestSupport.MigrateSqlServer(connectionString, 121).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_request') AND name = N'ApplicationStatusKey'"));

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_dataapproval_request_PendingApplication ON dbo.fn_dataapproval_request;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%121_DataApprovalRequestApplication.sql';
            """);

        Assert.IsTrue(Migration114To121RecoveryTestSupport.MigrateSqlServer(connectionString, 121).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_request') AND name = N'IX_fn_dataapproval_request_PendingApplication'"));
    }

    /// <summary>MySQL 重跑 121 时必须补回待应用扫描索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_data_approval_application_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To121RecoveryTestSupport.MigrateMySql(connectionString, 121).Successful);
        await using var connection = new MySqlConnection(
            Migration114To121RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_dataapproval_request' AND COLUMN_NAME = 'ApplicationStatusKey'"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_dataapproval_request DROP INDEX IX_fn_dataapproval_request_PendingApplication;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%121_DataApprovalRequestApplication.sql';
            """);

        Assert.IsTrue(Migration114To121RecoveryTestSupport.MigrateMySql(connectionString, 121).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_dataapproval_request' AND INDEX_NAME = 'IX_fn_dataapproval_request_PendingApplication'"));
    }
}
