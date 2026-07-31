using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 验证 045 在运行记录已存在但分页索引缺失时能够无损恢复。
/// </summary>
[TestClass]
public sealed class Migration045CodeGenerationRunRecoveryTests
{
    private const string IndexName =
        "IX_fn_codegeneration_run_StatusStarted";

    [TestMethod]
    public async Task SqlServer_run_migration_recovers_missing_index_without_dropping_data()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             INSERT INTO dbo.fn_codegeneration_run
                 (Id, TemplateId, TemplateVersion, OperationKind, Status,
                  ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                  ManifestSha256, ErrorCode, RequestedByUserId,
                  StartedAtUtc, FinishedAtUtc)
             VALUES
                 (NEWID(), NULL, NULL, 'preview', 'succeeded',
                  'catalog', 'product', REPLICATE('a', 64), 8,
                  REPLICATE('b', 64), NULL, NEWID(),
                  SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
             DROP INDEX {IndexName} ON dbo.fn_codegeneration_run;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%045_CodeGenerationRun.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_codegeneration_run
            WHERE EntityKey = 'product'
            """));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
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
                   OBJECT_ID(N'dbo.fn_codegeneration_run')
               AND indexObject.name = N'{IndexName}'
               AND (
                    (indexColumn.key_ordinal = 1
                     AND columnObject.name = N'Status')
                    OR (indexColumn.key_ordinal = 2
                        AND columnObject.name = N'StartedAtUtc')
                    OR (indexColumn.key_ordinal = 3
                        AND columnObject.name = N'Id')
               )
             """));

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON dbo.fn_codegeneration_run;
             CREATE INDEX {IndexName}
                 ON dbo.fn_codegeneration_run (StartedAtUtc DESC, Id);
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%045_CodeGenerationRun.sql';
             """);

        var repaired = await runner.MigrateAsync();

        Assert.AreEqual(1, repaired.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_codegeneration_run
            WHERE EntityKey = 'product'
            """));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
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
                   OBJECT_ID(N'dbo.fn_codegeneration_run')
               AND indexObject.name = N'{IndexName}'
               AND (
                    (indexColumn.key_ordinal = 1
                     AND columnObject.name = N'Status')
                    OR (indexColumn.key_ordinal = 2
                        AND columnObject.name = N'StartedAtUtc')
                    OR (indexColumn.key_ordinal = 3
                        AND columnObject.name = N'Id')
               )
             """));
    }

    [TestMethod]
    public async Task MySql_run_migration_recovers_missing_index_without_dropping_data()
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
        await connection.ExecuteAsync(
            $"""
             INSERT INTO fn_codegeneration_run
                 (Id, TemplateId, TemplateVersion, OperationKind, Status,
                  ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                  ManifestSha256, ErrorCode, RequestedByUserId,
                  StartedAtUtc, FinishedAtUtc)
             VALUES
                 (UNHEX(REPLACE(UUID(), '-', '')), NULL, NULL,
                  'preview', 'succeeded', 'catalog', 'product',
                  REPEAT('a', 64), 8, REPEAT('b', 64), NULL,
                  UNHEX(REPLACE(UUID(), '-', '')),
                  UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
             DROP INDEX {IndexName} ON fn_codegeneration_run;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%045_CodeGenerationRun.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_codegeneration_run
            WHERE EntityKey = 'product'
            """));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_codegeneration_run'
               AND INDEX_NAME = '{IndexName}'
               AND (
                    (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'Status')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'StartedAtUtc')
                    OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'Id')
               )
             """));

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON fn_codegeneration_run;
             CREATE INDEX {IndexName}
                 ON fn_codegeneration_run (StartedAtUtc DESC, Id);
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%045_CodeGenerationRun.sql';
             """);

        var repaired = await runner.MigrateAsync();

        Assert.AreEqual(1, repaired.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_codegeneration_run
            WHERE EntityKey = 'product'
            """));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_codegeneration_run'
               AND INDEX_NAME = '{IndexName}'
               AND (
                    (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'Status')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'StartedAtUtc')
                    OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'Id')
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
