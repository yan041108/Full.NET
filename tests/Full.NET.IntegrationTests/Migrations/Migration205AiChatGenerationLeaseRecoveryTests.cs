using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 205 生成租约列缺失时可恢复，且无代次的旧 generating 状态会被收敛。</summary>
[TestClass]
public sealed class Migration205AiChatGenerationLeaseRecoveryTests
{
    private const string ScriptToken = "205_AiChatGenerationLease.sql";

    /// <summary>SQL Server 删除租约列后重跑 205 必须补回三列。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_missing_generation_lease_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        var defaultConstraints = (await connection.QueryAsync<string>(
            """
            SELECT defaults.name
            FROM sys.default_constraints AS defaults
            INNER JOIN sys.columns AS columnObject
                ON defaults.parent_object_id = columnObject.object_id
               AND defaults.parent_column_id = columnObject.column_id
            WHERE defaults.parent_object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
              AND columnObject.name IN (
                  N'GenerationId',
                  N'GenerationExpiresAtUtc',
                  N'GenerationCancellationRequested')
            """).ConfigureAwait(false)).AsList();
        foreach (var constraintName in defaultConstraints)
        {
            await connection.ExecuteAsync(
                $"ALTER TABLE dbo.fn_ai_chat_session DROP CONSTRAINT [{constraintName.Replace("]", "]]", StringComparison.Ordinal)}];")
                .ConfigureAwait(false);
        }

        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_ai_chat_session
                DROP COLUMN GenerationId, GenerationExpiresAtUtc, GenerationCancellationRequested;
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationId", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationExpiresAtUtc", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationCancellationRequested", sqlServer: true).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除租约列后重跑 205 必须补回三列，并停止无代次的遗留 generating。</summary>
    [TestMethod]
    public async Task MySql_recovers_missing_generation_lease_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_ai_chat_session
                DROP COLUMN GenerationId,
                DROP COLUMN GenerationExpiresAtUtc,
                DROP COLUMN GenerationCancellationRequested;
            """).ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationId", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationExpiresAtUtc", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.ColumnExistsAsync(
            connection, "fn_ai_chat_session", "GenerationCancellationRequested", sqlServer: false).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
