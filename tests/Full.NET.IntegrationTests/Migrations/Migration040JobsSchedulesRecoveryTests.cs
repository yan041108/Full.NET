using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 040 能从计划索引或执行关联列缺失的半完成状态恢复。</summary>
[TestClass]
public sealed class Migration040JobsSchedulesRecoveryTests
{
    [TestMethod]
    public async Task MySql_jobs_schedule_migration_recovers_missing_index_and_execution_columns()
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
        await AssertMySqlShapeAsync(connection);
        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_jobs_schedule_Due
                ON fn_jobs_schedule;
            ALTER TABLE fn_jobs_execution
                DROP COLUMN ScheduledForUtc;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%040_JobsSchedules.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlShapeAsync(connection);
    }

    [TestMethod]
    public async Task SqlServer_jobs_schedule_migration_recovers_missing_index_and_execution_columns()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await AssertSqlServerShapeAsync(connection);
        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_jobs_schedule_Due
                ON dbo.fn_jobs_schedule;
            ALTER TABLE dbo.fn_jobs_execution
                DROP COLUMN ScheduledForUtc;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%040_JobsSchedules.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerShapeAsync(connection);
    }

    private static async Task AssertMySqlShapeAsync(
        MySqlConnection connection)
    {
        var tableCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_jobs_schedule'
            """);
        var columnCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_jobs_execution'
              AND COLUMN_NAME IN ('JobScheduleId', 'ScheduledForUtc')
            """);
        var indexColumns = (await connection.QueryAsync<string>(
            """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_jobs_schedule'
              AND INDEX_NAME = 'IX_fn_jobs_schedule_Due'
            ORDER BY SEQ_IN_INDEX
            """)).ToArray();

        Assert.AreEqual(1, tableCount);
        Assert.AreEqual(2, columnCount);
        CollectionAssert.AreEqual(
            new[] { "TenantId", "IsEnabled", "NextExecutionAtUtc", "Id" },
            indexColumns);
    }

    private static async Task AssertSqlServerShapeAsync(
        SqlConnection connection)
    {
        var tableCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
            """);
        var columnCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_execution')
              AND name IN (N'JobScheduleId', N'ScheduledForUtc')
            """);
        var indexColumns = (await connection.QueryAsync<string>(
            """
            SELECT columnObject.name
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
               AND indexColumn.key_ordinal > 0
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id =
                  OBJECT_ID(N'dbo.fn_jobs_schedule')
              AND indexObject.name = N'IX_fn_jobs_schedule_Due'
            ORDER BY indexColumn.key_ordinal
            """)).ToArray();

        Assert.AreEqual(1, tableCount);
        Assert.AreEqual(2, columnCount);
        CollectionAssert.AreEqual(
            new[] { "NextExecutionAtUtc", "Id" },
            indexColumns);
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
