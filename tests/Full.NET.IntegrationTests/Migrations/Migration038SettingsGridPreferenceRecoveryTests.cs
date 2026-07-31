using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 038 在唯一索引缺失或形状错误时仍能收敛。</summary>
[TestClass]
public sealed class Migration038SettingsGridPreferenceRecoveryTests
{
    private const string IndexName =
        "UX_fn_settings_user_grid_preference_UserGrid";

    [TestMethod]
    public async Task MySql_grid_preference_migration_recovers_missing_or_malformed_unique_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON fn_settings_user_grid_preference;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%038_SettingsGridPreference.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlIndexAsync(connection);

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON fn_settings_user_grid_preference;
             CREATE UNIQUE INDEX {IndexName}
                 ON fn_settings_user_grid_preference(UserId, GridKey(16));
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%038_SettingsGridPreference.sql';
             """);
        recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlIndexAsync(connection);
    }

    [TestMethod]
    public async Task SqlServer_grid_preference_migration_recovers_missing_or_malformed_unique_index()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName}
                 ON dbo.fn_settings_user_grid_preference;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%038_SettingsGridPreference.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerIndexAsync(connection);

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName}
                 ON dbo.fn_settings_user_grid_preference;
             CREATE UNIQUE INDEX {IndexName}
                 ON dbo.fn_settings_user_grid_preference(UserId, GridKey)
                 WHERE Version > 0;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%038_SettingsGridPreference.sql';
             """);
        recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerIndexAsync(connection);
    }

    private static async Task AssertMySqlIndexAsync(MySqlConnection connection)
    {
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_settings_user_grid_preference'
               AND INDEX_NAME = '{IndexName}'
               AND NON_UNIQUE = 0
               AND SUB_PART IS NULL
               AND (
                    (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'UserId')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'GridKey')
               )
             """));
    }

    private static async Task AssertSqlServerIndexAsync(SqlConnection connection)
    {
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM sys.indexes AS indexObject
             INNER JOIN sys.index_columns AS indexColumn
                 ON indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
             INNER JOIN sys.columns AS columnObject
                 ON columnObject.object_id = indexColumn.object_id
                AND columnObject.column_id = indexColumn.column_id
             WHERE indexObject.object_id =
                   OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
               AND indexObject.name = N'{IndexName}'
               AND indexObject.is_unique = 1
               AND indexObject.has_filter = 0
               AND indexObject.is_disabled = 0
               AND (
                    (indexColumn.key_ordinal = 1
                     AND columnObject.name = N'UserId')
                    OR (indexColumn.key_ordinal = 2
                        AND columnObject.name = N'GridKey')
               )
             """));
    }

    private static DbUpMigrationRunner CreateRunner(
        DatabaseProvider provider,
        string connectionString) =>
        new(
            Options.Create(new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
}
