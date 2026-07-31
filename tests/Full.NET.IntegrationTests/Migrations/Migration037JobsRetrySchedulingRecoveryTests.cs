using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 037 在到期列已存在但待领取索引缺失时仍能收敛。</summary>
[TestClass]
public sealed class Migration037JobsRetrySchedulingRecoveryTests
{
    private const string PendingIndexName =
        "IX_fn_jobs_execution_PendingNextAttemptLease";

    [TestMethod]
    public async Task MySql_jobs_retry_scheduling_migration_recovers_partial_state()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await AssertMySqlSchemaAsync(connection);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {PendingIndexName} ON fn_jobs_execution;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%037_JobsRetryScheduling.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlSchemaAsync(connection);
    }

    [TestMethod]
    public async Task SqlServer_jobs_retry_scheduling_migration_recovers_partial_state()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await AssertSqlServerSchemaAsync(connection);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {PendingIndexName} ON dbo.fn_jobs_execution;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%037_JobsRetryScheduling.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerSchemaAsync(connection);
    }

    private static async Task AssertMySqlSchemaAsync(MySqlConnection connection)
    {
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_jobs_execution'
              AND COLUMN_NAME = 'NextAttemptAtUtc'
              AND DATA_TYPE = 'datetime'
              AND DATETIME_PRECISION = 6
              AND IS_NULLABLE = 'YES'
            """));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_jobs_execution'
              AND INDEX_NAME = 'IX_fn_jobs_execution_PendingNextAttemptLease'
              AND COLUMN_NAME IN
                  ('Status', 'NextAttemptAtUtc', 'LeaseExpiresAtUtc', 'CreatedAtUtc')
            """));
    }

    private static async Task AssertSqlServerSchemaAsync(
        SqlConnection connection)
    {
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'fn_jobs_execution'
              AND COLUMN_NAME = 'NextAttemptAtUtc'
              AND DATA_TYPE = 'datetimeoffset'
              AND DATETIME_PRECISION = 7
              AND IS_NULLABLE = 'YES'
            """));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_jobs_execution')
              AND indexObject.name =
                  N'IX_fn_jobs_execution_PendingNextAttemptLease'
              AND indexObject.filter_definition = N'([Status]=''pending'')'
              AND columnObject.name IN
                  (N'Status', N'NextAttemptAtUtc', N'LeaseExpiresAtUtc',
                   N'CreatedAtUtc')
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
