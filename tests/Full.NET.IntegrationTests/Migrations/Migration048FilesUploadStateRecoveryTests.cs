using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 048 能从状态列已存在但可空、空值与检查约束缺失的半完成状态恢复。</summary>
[TestClass]
public sealed class Migration048FilesUploadStateRecoveryTests
{
    [TestMethod]
    public async Task MySql_files_upload_state_migration_recovers_partial_state()
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
            ALTER TABLE fn_files_file
                DROP CHECK CK_fn_files_file_StorageState,
                MODIFY COLUMN StorageState varchar(16)
                    CHARACTER SET ascii COLLATE ascii_bin NULL;
            INSERT INTO fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
                 ProviderKey, StorageKey, ContentHash, StorageState,
                 CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, 'partial.bin', 'application/octet-stream', 1,
                 'local', @StorageKey, NULL, NULL,
                 UTC_TIMESTAMP(6), @FileId, NULL);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%048_FilesUploadState.sql';
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/partial/{fileId:N}",
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlShapeAsync(connection);
        Assert.AreEqual(
            "ready",
            await connection.ExecuteScalarAsync<string>(
                "SELECT StorageState FROM fn_files_file WHERE Id = @FileId",
                new { FileId = fileId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM schemaversions WHERE ScriptName LIKE '%048_FilesUploadState.sql'"));
        _ = await Assert.ThrowsAsync<MySqlException>(
            () => connection.ExecuteAsync(
                "UPDATE fn_files_file SET StorageState = 'invalid' WHERE Id = @FileId",
                new { FileId = fileId }));
    }

    [TestMethod]
    public async Task SqlServer_files_upload_state_migration_recovers_partial_state()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await AssertSqlServerShapeAsync(connection);
        var fileId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_files_file
                DROP CONSTRAINT CK_fn_files_file_StorageState;
            ALTER TABLE dbo.fn_files_file
                ALTER COLUMN StorageState varchar(16) NULL;
            INSERT INTO dbo.fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes,
                 ProviderKey, StorageKey, ContentHash, StorageState,
                 CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, N'partial.bin', 'application/octet-stream', 1,
                 'local', @StorageKey, NULL, NULL,
                 SYSUTCDATETIME(), @FileId, NULL);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%048_FilesUploadState.sql';
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/partial/{fileId:N}",
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerShapeAsync(connection);
        Assert.AreEqual(
            "ready",
            await connection.ExecuteScalarAsync<string>(
                "SELECT StorageState FROM dbo.fn_files_file WHERE Id = @FileId",
                new { FileId = fileId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.SchemaVersions WHERE ScriptName LIKE '%048_FilesUploadState.sql'"));
        _ = await Assert.ThrowsAsync<SqlException>(
            () => connection.ExecuteAsync(
                "UPDATE dbo.fn_files_file SET StorageState = 'invalid' WHERE Id = @FileId",
                new { FileId = fileId }));
    }

    private static async Task AssertMySqlShapeAsync(MySqlConnection connection)
    {
        var column = await connection.QuerySingleAsync<MySqlColumnShape>(
            """
            SELECT IS_NULLABLE AS IsNullable,
                   CHARACTER_SET_NAME AS CharacterSetName,
                   COLLATION_NAME AS CollationName
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_files_file'
              AND COLUMN_NAME = 'StorageState'
            """);
        var constraintCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_files_file'
              AND CONSTRAINT_NAME = 'CK_fn_files_file_StorageState'
              AND CONSTRAINT_TYPE = 'CHECK'
            """);

        Assert.AreEqual("NO", column.IsNullable);
        Assert.AreEqual("ascii", column.CharacterSetName);
        Assert.AreEqual("ascii_bin", column.CollationName);
        Assert.AreEqual(1, constraintCount);
    }

    private static async Task AssertSqlServerShapeAsync(SqlConnection connection)
    {
        var column = await connection.QuerySingleAsync<SqlServerColumnShape>(
            """
            SELECT columnObject.is_nullable AS IsNullable,
                   TYPE_NAME(columnObject.user_type_id) AS TypeName,
                   CAST(columnObject.max_length AS int) AS MaxLength,
                   columnObject.collation_name AS CollationName
            FROM sys.columns AS columnObject
            WHERE columnObject.object_id = OBJECT_ID(N'dbo.fn_files_file')
              AND columnObject.name = N'StorageState'
            """);
        var constraintCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.fn_files_file')
              AND name = N'CK_fn_files_file_StorageState'
            """);

        Assert.IsFalse(column.IsNullable);
        Assert.AreEqual("varchar", column.TypeName);
        Assert.AreEqual(16, column.MaxLength);
        Assert.AreEqual("Latin1_General_100_BIN2", column.CollationName);
        Assert.AreEqual(1, constraintCount);
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

    private sealed record MySqlColumnShape(
        string IsNullable,
        string CharacterSetName,
        string CollationName);

    private sealed record SqlServerColumnShape(
        bool IsNullable,
        string TypeName,
        int MaxLength,
        string CollationName);
}
