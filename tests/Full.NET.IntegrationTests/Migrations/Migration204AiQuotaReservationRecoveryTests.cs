using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 204 配额预留表在删除后可幂等恢复。</summary>
[TestClass]
public sealed class Migration204AiQuotaReservationRecoveryTests
{
    private const string TableName = "fn_ai_quota_reservation";
    private const string ScriptToken = "204_AiQuotaReservation.sql";

    /// <summary>SQL Server 删除预留表后重跑 204 必须重建。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_missing_quota_reservation_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync($"DROP TABLE dbo.{TableName};").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.TableExistsAsync(connection, TableName, sqlServer: true)
            .ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除预留表后重跑 204 必须重建。</summary>
    [TestMethod]
    public async Task MySql_recovers_missing_quota_reservation_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync($"DROP TABLE {TableName};").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.TableExistsAsync(connection, TableName, sqlServer: false)
            .ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
