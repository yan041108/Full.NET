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
/// 验证 044 在模板数据已存在但列表索引缺失时能够无损恢复。
/// </summary>
[TestClass]
public sealed class Migration044CodeGenerationTemplateRecoveryTests
{
    private const string IndexName =
        "IX_fn_codegeneration_template_ActiveUpdatedCreated";

    [TestMethod]
    public async Task MySql_template_migration_recovers_missing_index_without_dropping_data()
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
             INSERT INTO fn_codegeneration_template
                 (Id, Name, Description, SchemaJson, SchemaSha256,
                  CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
                  DeletedAtUtc, DeletedByUserId, IsDeleted, Version)
             VALUES
                 (UNHEX(REPLACE(UUID(), '-', '')), 'preserved', NULL,
                  JSON_OBJECT(),
                  REPEAT('a', 64), UTC_TIMESTAMP(6),
                  UNHEX(REPLACE(UUID(), '-', '')), NULL, NULL,
                  NULL, NULL, 0, 1);
             DROP INDEX {IndexName} ON fn_codegeneration_template;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%044_CodeGenerationTemplate.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_codegeneration_template
            WHERE Name = 'preserved'
            """));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_codegeneration_template'
               AND INDEX_NAME = '{IndexName}'
               AND (
                    (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'IsDeleted')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'UpdatedAtUtc')
                    OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'CreatedAtUtc')
                    OR (SEQ_IN_INDEX = 4 AND COLUMN_NAME = 'Id')
               )
             """));
    }

    [TestMethod]
    public async Task SqlServer_template_migration_recovers_missing_index_without_dropping_data()
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
             INSERT INTO dbo.fn_codegeneration_template
                 (Id, Name, Description, SchemaJson, SchemaSha256,
                  CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
                  DeletedAtUtc, DeletedByUserId, IsDeleted, Version)
             VALUES
                 (NEWID(), N'preserved', NULL, NCHAR(123) + NCHAR(125),
                  REPLICATE('a', 64),
                  SYSDATETIMEOFFSET(), NEWID(), NULL, NULL,
                  NULL, NULL, 0, 1);
             DROP INDEX {IndexName} ON dbo.fn_codegeneration_template;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%044_CodeGenerationTemplate.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_codegeneration_template
            WHERE Name = N'preserved'
            """));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(
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
                   OBJECT_ID(N'dbo.fn_codegeneration_template')
               AND indexObject.name = N'{IndexName}'
               AND (
                    (indexColumn.key_ordinal = 1
                     AND columnObject.name = N'IsDeleted')
                    OR (indexColumn.key_ordinal = 2
                        AND columnObject.name = N'UpdatedAtUtc')
                    OR (indexColumn.key_ordinal = 3
                        AND columnObject.name = N'CreatedAtUtc')
                    OR (indexColumn.key_ordinal = 4
                        AND columnObject.name = N'Id')
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
