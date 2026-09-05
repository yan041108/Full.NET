using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证多人审批结构可在 SQL Server 与 MySQL 完整创建并恢复缺失索引。</summary>
[TestClass]
public sealed class Migration113WorkflowMultiApprovalRecoveryTests
{
    /// <summary>SQL Server 必须创建步骤快照、确定性动作结果和审批席位结构。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_creates_multi_approval_schema_and_recovers_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(MigrateSqlServerThrough113(connectionString).Successful);
        await using var connection = new SqlConnection(connectionString);

        Assert.AreEqual(5, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM sys.columns
            WHERE (object_id = OBJECT_ID(N'dbo.fn_workflow_step') AND name IN (N'ApprovalModeKey', N'RequiredApprovalCount', N'ApprovalSlotCount'))
               OR (object_id = OBJECT_ID(N'dbo.fn_workflow_action_record') AND name IN (N'ResultStatusKey', N'ResultTodoId'))
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot')"));

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_approval_slot_Step_Decision ON dbo.fn_workflow_approval_slot;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%113_WorkflowMultiApproval.sql';
            """);
        Assert.IsTrue(MigrateSqlServerThrough113(connectionString).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name = N'IX_fn_workflow_approval_slot_Step_Decision'"));
    }

    /// <summary>MySQL 必须创建步骤快照、确定性动作结果和审批席位结构。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_creates_multi_approval_schema_and_recovers_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(MigrateMySqlThrough113(connectionString).Successful);
        await using var connection = new MySqlConnection(MySqlConnectionStringPolicy.Create(
            connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

        Assert.AreEqual(5, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND ((TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME IN ('ApprovalModeKey', 'RequiredApprovalCount', 'ApprovalSlotCount'))
                OR (TABLE_NAME = 'fn_workflow_action_record' AND COLUMN_NAME IN ('ResultStatusKey', 'ResultTodoId')))
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_approval_slot'"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_approval_slot DROP INDEX IX_fn_workflow_approval_slot_Step_Decision;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%113_WorkflowMultiApproval.sql';
            """);
        Assert.IsTrue(MigrateMySqlThrough113(connectionString).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_approval_slot' AND INDEX_NAME = 'IX_fn_workflow_approval_slot_Step_Decision'"));
    }

    /// <summary>执行 SQL Server 001 至 113 迁移。</summary>
    /// <param name="connectionString">目标数据库连接字符串。</param>
    /// <returns>DbUp 升级结果。</returns>
    private static DbUp.Engine.DatabaseUpgradeResult MigrateSqlServerThrough113(string connectionString) =>
        DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 113))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();

    /// <summary>执行 MySQL 001 至 113 迁移。</summary>
    /// <param name="connectionString">目标数据库连接字符串。</param>
    /// <returns>DbUp 升级结果。</returns>
    private static DbUp.Engine.DatabaseUpgradeResult MigrateMySqlThrough113(string connectionString) =>
        DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
            .WithPreprocessor(new Migration111WorkflowTodoTimeoutPolicyRecoveryTests.Through111MySqlCompatibilityPreprocessor())
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 113))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
}
