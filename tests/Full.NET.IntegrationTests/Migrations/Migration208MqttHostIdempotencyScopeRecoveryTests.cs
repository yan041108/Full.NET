using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 208 在回退到旧 TenantId 唯一索引后，仍能恢复 Host 空租户哨兵索引。</summary>
[TestClass]
public sealed class Migration208MqttHostIdempotencyScopeRecoveryTests
{
    private const string ScriptToken = "208_MqttHostIdempotencyScope.sql";
    private const string NewIndex = "UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey";
    private const string OldIndex = "UX_fn_mqtt_message_TenantId_IdempotencyKey";

    /// <summary>SQL Server 删除哨兵列与新索引后重跑 208 必须恢复。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_host_scope_unique_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
            DROP INDEX {NewIndex} ON dbo.fn_mqtt_message;
            ALTER TABLE dbo.fn_mqtt_message DROP COLUMN ScopeTenantKey;
            CREATE UNIQUE INDEX {OldIndex}
                ON dbo.fn_mqtt_message (TenantId, IdempotencyKey)
                WHERE IdempotencyKey IS NOT NULL;
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_mqtt_message", "ScopeTenantKey", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_mqtt_message", NewIndex, sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(0, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_mqtt_message", OldIndex, sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除哨兵列与新索引后重跑 208 必须恢复。</summary>
    [TestMethod]
    public async Task MySql_recovers_host_scope_unique_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
            ALTER TABLE fn_mqtt_message DROP INDEX {NewIndex};
            ALTER TABLE fn_mqtt_message DROP COLUMN ScopeTenantKey;
            CREATE UNIQUE INDEX {OldIndex} ON fn_mqtt_message (TenantId, IdempotencyKey);
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_mqtt_message", "ScopeTenantKey", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_mqtt_message", NewIndex, sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(0, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection, "fn_mqtt_message", OldIndex, sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
