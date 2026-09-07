using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 207 任务租约列与领取索引缺失时可恢复，报表 queued 状态可重跑补齐。</summary>
[TestClass]
public sealed class Migration207TaskExecutionLeaseRecoveryTests
{
    private const string ScriptToken = "207_TaskExecutionLease.sql";
    private const string ImportIndex = "IX_fn_import_export_task_TenantId_StatusKey_LeaseExpiresAtUtc";
    private const string ReportIndex = "IX_fn_reporting_export_task_TenantId_StatusKey_LeaseExpiresAtUtc";

    /// <summary>SQL Server 删除租约列与索引后重跑 207 必须补回。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_missing_task_lease_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
            DROP INDEX {ImportIndex} ON dbo.fn_import_export_task;
            DROP INDEX {ReportIndex} ON dbo.fn_reporting_export_task;
            ALTER TABLE dbo.fn_import_export_task DROP COLUMN LeaseId, LeaseExpiresAtUtc;
            ALTER TABLE dbo.fn_reporting_export_task DROP COLUMN LeaseId, LeaseExpiresAtUtc, ActorPermissionCodesJson;
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_import_export_task", "LeaseId", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_import_export_task", ImportIndex, sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_reporting_export_task", "ActorPermissionCodesJson", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除租约列与索引后重跑 207 必须补回。</summary>
    [TestMethod]
    public async Task MySql_recovers_missing_task_lease_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
            ALTER TABLE fn_import_export_task DROP INDEX {ImportIndex};
            ALTER TABLE fn_reporting_export_task DROP INDEX {ReportIndex};
            ALTER TABLE fn_import_export_task DROP COLUMN LeaseId, DROP COLUMN LeaseExpiresAtUtc;
            ALTER TABLE fn_reporting_export_task
                DROP COLUMN LeaseId,
                DROP COLUMN LeaseExpiresAtUtc,
                DROP COLUMN ActorPermissionCodesJson;
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_import_export_task", "LeaseId", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_import_export_task", ImportIndex, sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_reporting_export_task", "ActorPermissionCodesJson", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
