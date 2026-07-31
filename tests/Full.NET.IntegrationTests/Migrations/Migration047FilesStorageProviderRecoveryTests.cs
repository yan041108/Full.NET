using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 047 能从 Provider 列已存在但未收紧、唯一索引缺失的半完成状态恢复。</summary>
[TestClass]
public sealed class Migration047FilesStorageProviderRecoveryTests
{
    [TestMethod]
    public async Task MySql_files_provider_migration_recovers_nullable_column_and_missing_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await AssertMySqlShapeAsync(connection);
        var fileId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_files_file_ProviderKey_StorageKey
                ON fn_files_file;
            ALTER TABLE fn_files_file
                MODIFY COLUMN ProviderKey varchar(64)
                    CHARACTER SET ascii COLLATE ascii_bin NULL;
            INSERT INTO fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
                 ProviderKey, StorageKey, ContentHash, StorageState, CreatedAtUtc,
                 CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, 'legacy.bin', 'application/octet-stream', 1,
                 NULL, @StorageKey, NULL, 'ready', UTC_TIMESTAMP(6), @FileId, NULL);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%047_FilesStorageProvider.sql';
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/legacy/{fileId:N}",
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlShapeAsync(connection);
        Assert.AreEqual(
            "local",
            await connection.ExecuteScalarAsync<string>(
                "SELECT ProviderKey FROM fn_files_file WHERE Id = @FileId",
                new { FileId = fileId }));
    }

    [TestMethod]
    public async Task SqlServer_files_provider_migration_recovers_nullable_column_and_missing_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await AssertSqlServerShapeAsync(connection);
        var fileId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_files_file_ProviderKey_StorageKey
                ON dbo.fn_files_file;
            ALTER TABLE dbo.fn_files_file
                ALTER COLUMN ProviderKey varchar(64) NULL;
            INSERT INTO dbo.fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
                 ProviderKey, StorageKey, ContentHash, StorageState, CreatedAtUtc,
                 CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, N'legacy.bin', 'application/octet-stream', 1,
                 NULL, @StorageKey, NULL, 'ready', SYSUTCDATETIME(), @FileId, NULL);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%047_FilesStorageProvider.sql';
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/legacy/{fileId:N}",
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerShapeAsync(connection);
        Assert.AreEqual(
            "local",
            await connection.ExecuteScalarAsync<string>(
                "SELECT ProviderKey FROM dbo.fn_files_file WHERE Id = @FileId",
                new { FileId = fileId }));
    }

    private static async Task AssertMySqlShapeAsync(MySqlConnection connection)
    {
        var isNullable = await connection.ExecuteScalarAsync<string>(
            """
            SELECT IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_files_file'
              AND COLUMN_NAME = 'ProviderKey'
            """);
        var indexRows = (await connection.QueryAsync<MySqlIndexRow>(
            """
            SELECT COLUMN_NAME AS ColumnName,
                   NON_UNIQUE AS NonUnique
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_files_file'
              AND INDEX_NAME = 'UX_fn_files_file_ProviderKey_StorageKey'
            ORDER BY SEQ_IN_INDEX
            """)).ToArray();

        Assert.AreEqual("NO", isNullable);
        CollectionAssert.AreEqual(
            new[] { "ProviderKey", "StorageKey" },
            indexRows.Select(row => row.ColumnName).ToArray());
        Assert.IsTrue(indexRows.All(row => row.NonUnique == 0));
    }

    private static async Task AssertSqlServerShapeAsync(SqlConnection connection)
    {
        var isNullable = await connection.ExecuteScalarAsync<bool>(
            """
            SELECT is_nullable
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.fn_files_file')
              AND name = N'ProviderKey'
            """);
        var indexRows = (await connection.QueryAsync<SqlServerIndexRow>(
            """
            SELECT columnObject.name AS ColumnName,
                   indexObject.is_unique AS IsUnique,
                   indexObject.has_filter AS HasFilter
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
               AND indexColumn.key_ordinal > 0
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_files_file')
              AND indexObject.name = N'UX_fn_files_file_ProviderKey_StorageKey'
            ORDER BY indexColumn.key_ordinal
            """)).ToArray();

        Assert.IsFalse(isNullable);
        CollectionAssert.AreEqual(
            new[] { "ProviderKey", "StorageKey" },
            indexRows.Select(row => row.ColumnName).ToArray());
        Assert.IsTrue(indexRows.All(row => row.IsUnique));
        Assert.IsTrue(indexRows.All(row => !row.HasFilter));
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

    private sealed record MySqlIndexRow(string ColumnName, int NonUnique);

    private sealed record SqlServerIndexRow(
        string ColumnName,
        bool IsUnique,
        bool HasFilter);
}
