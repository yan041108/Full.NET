using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 119 数据审批场景绑定表可从索引缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration119DataApprovalScenarioRecoveryTests
{
    /// <summary>SQL Server 重跑 119 时必须补回场景唯一索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_data_approval_scenario_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To119RecoveryTestSupport.MigrateSqlServer(connectionString, 119).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario')"));

        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_dataapproval_scenario_ScenarioKey ON dbo.fn_dataapproval_scenario;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%119_DataApprovalScenario.sql';
            """);

        Assert.IsTrue(Migration114To119RecoveryTestSupport.MigrateSqlServer(connectionString, 119).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_scenario') AND name = N'UX_fn_dataapproval_scenario_ScenarioKey'"));
    }

    /// <summary>MySQL 重跑 119 时必须补回场景唯一索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_data_approval_scenario_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To119RecoveryTestSupport.MigrateMySql(connectionString, 119).Successful);
        await using var connection = new MySqlConnection(
            Migration114To119RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_dataapproval_scenario'"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_dataapproval_scenario DROP INDEX UX_fn_dataapproval_scenario_ScenarioKey;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%119_DataApprovalScenario.sql';
            """);

        Assert.IsTrue(Migration114To119RecoveryTestSupport.MigrateMySql(connectionString, 119).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_dataapproval_scenario' AND INDEX_NAME = 'UX_fn_dataapproval_scenario_ScenarioKey'"));
    }
}
