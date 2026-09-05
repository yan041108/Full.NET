using DbUp;
using DbUp.Engine;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>为 114 至 122 专用恢复测试提供双库迁移执行入口。</summary>
internal static class Migration114To122RecoveryTestSupport
{
    /// <summary>执行 SQL Server 迁移至指定版本。</summary>
    /// <param name="connectionString">目标数据库连接字符串。</param>
    /// <param name="migrationNumber">最后一个允许执行的迁移编号。</param>
    /// <returns>DbUp 升级结果。</returns>
    internal static DatabaseUpgradeResult MigrateSqlServer(string connectionString, int migrationNumber) =>
        DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, migrationNumber))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();

    /// <summary>执行 MySQL 迁移至指定版本。</summary>
    /// <param name="connectionString">目标数据库连接字符串。</param>
    /// <param name="migrationNumber">最后一个允许执行的迁移编号。</param>
    /// <returns>DbUp 升级结果。</returns>
    internal static DatabaseUpgradeResult MigrateMySql(string connectionString, int migrationNumber) =>
        DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
            .WithPreprocessor(new Migration111WorkflowTodoTimeoutPolicyRecoveryTests.Through111MySqlCompatibilityPreprocessor())
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, migrationNumber))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();

    /// <summary>构造不允许用户变量的 MySQL 验证连接字符串。</summary>
    /// <param name="connectionString">测试数据库原始连接字符串。</param>
    /// <returns>采用 Binary16 Guid 约定的连接字符串。</returns>
    internal static string MySqlQueryConnectionString(string connectionString) =>
        MySqlConnectionStringPolicy.Create(
            connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false);
}
