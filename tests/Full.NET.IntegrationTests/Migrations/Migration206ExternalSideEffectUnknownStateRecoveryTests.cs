using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 206 在 CHECK 被回退后仍能恢复 provider_unknown 允许值。</summary>
[TestClass]
public sealed class Migration206ExternalSideEffectUnknownStateRecoveryTests
{
    private const string ScriptToken = "206_ExternalSideEffectUnknownState.sql";

    /// <summary>SQL Server 删除支付状态 CHECK 后重跑 206 必须允许 provider_unknown。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_provider_unknown_check_constraints()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            "ALTER TABLE dbo.fn_payment_order DROP CONSTRAINT CK_fn_payment_order_TradeStateKey;").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        var definition = await connection.QuerySingleAsync<string>(
            """
            SELECT definition FROM sys.check_constraints
            WHERE name = N'CK_fn_payment_order_TradeStateKey'
            """).ConfigureAwait(false);
        Assert.Contains("provider_unknown", definition);
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除支付状态 CHECK 后重跑 206 必须允许 provider_unknown。</summary>
    [TestMethod]
    public async Task MySql_recovers_provider_unknown_check_constraints()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            "ALTER TABLE fn_payment_order DROP CHECK CK_fn_payment_order_TradeStateKey;").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        var clause = await connection.QuerySingleAsync<string>(
            """
            SELECT CHECK_CLAUSE FROM information_schema.CHECK_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_NAME = 'CK_fn_payment_order_TradeStateKey'
            """).ConfigureAwait(false);
        Assert.Contains("provider_unknown", clause);
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
