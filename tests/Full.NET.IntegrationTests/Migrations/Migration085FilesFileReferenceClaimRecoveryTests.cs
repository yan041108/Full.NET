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
/// Verifies migration 085 can recreate the file reference claim table after accidental drop.
/// </summary>
[TestClass]
public sealed class Migration085FilesFileReferenceClaimRecoveryTests
{
    private const string TableName = "fn_files_file_reference_claim";
    private const string MigrationScriptToken = "085_FilesFileReferenceClaim.sql";

    [TestMethod]
    public async Task SqlServer_file_reference_claim_migration_recovers_missing_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        var fileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await SeedSqlServerFileAsync(connection, fileId, now);
        await SeedSqlServerClaimAsync(connection, claimId, fileId, now);
        await connection.ExecuteAsync(
            $"""
            DROP TABLE dbo.{TableName};
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await SqlServerTableExistsAsync(connection));
        await SeedSqlServerClaimAsync(connection, Guid.NewGuid(), fileId, now.AddMinutes(1));
        Assert.AreEqual(1, await CountSqlServerClaimsAsync(connection, fileId));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_file_reference_claim_migration_recovers_missing_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        var fileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await SeedMySqlFileAsync(connection, fileId, now);
        await SeedMySqlClaimAsync(connection, claimId, fileId, now);
        await connection.ExecuteAsync(
            $"""
            DROP TABLE {TableName};
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await MySqlTableExistsAsync(connection));
        await SeedMySqlClaimAsync(connection, Guid.NewGuid(), fileId, now.AddMinutes(1));
        Assert.AreEqual(1, await CountMySqlClaimsAsync(connection, fileId));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task SeedSqlServerFileAsync(SqlConnection connection, Guid fileId, DateTimeOffset now) =>
        connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey,
                 ContentHash, StorageState, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, N'claim-recovery.bin', N'application/octet-stream', 1,
                 N'local', @StorageKey, NULL, N'ready', @Now, @CreatedByUserId, NULL);
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/recovery/{fileId:N}",
                Now = now,
                CreatedByUserId = Guid.NewGuid(),
            });

    private static Task SeedMySqlFileAsync(MySqlConnection connection, Guid fileId, DateTimeOffset now) =>
        connection.ExecuteAsync(
            """
            INSERT INTO fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey,
                 ContentHash, StorageState, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
            VALUES
                (@FileId, NULL, 'claim-recovery.bin', 'application/octet-stream', 1,
                 'local', @StorageKey, NULL, 'ready', @Now, @CreatedByUserId, NULL);
            """,
            new
            {
                FileId = fileId,
                StorageKey = $"host/recovery/{fileId:N}",
                Now = now,
                CreatedByUserId = Guid.NewGuid(),
            });

    private static Task SeedSqlServerClaimAsync(
        SqlConnection connection,
        Guid claimId,
        Guid fileId,
        DateTimeOffset now) =>
        connection.ExecuteAsync(
            $"""
            INSERT INTO dbo.{TableName}
                (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State,
                 ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@ClaimId, @IdempotencyKey, @FileId, N'document', @ConsumerReferenceId,
                 N'active', NULL, 1, @Now, @Now);
            """,
            new
            {
                ClaimId = claimId,
                IdempotencyKey = $"document-version:{claimId:D}",
                FileId = fileId,
                ConsumerReferenceId = Guid.NewGuid(),
                Now = now,
            });

    private static Task SeedMySqlClaimAsync(
        MySqlConnection connection,
        Guid claimId,
        Guid fileId,
        DateTimeOffset now) =>
        connection.ExecuteAsync(
            $"""
            INSERT INTO {TableName}
                (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State,
                 ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@ClaimId, @IdempotencyKey, @FileId, 'document', @ConsumerReferenceId,
                 'active', NULL, 1, @Now, @Now);
            """,
            new
            {
                ClaimId = claimId,
                IdempotencyKey = $"document-version:{claimId:D}",
                FileId = fileId,
                ConsumerReferenceId = Guid.NewGuid(),
                Now = now,
            });

    private static async Task<bool> SqlServerTableExistsAsync(SqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = N'dbo'
              AND TABLE_NAME = @TableName
            """,
            new { TableName }) == 1;

    private static async Task<bool> MySqlTableExistsAsync(MySqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @TableName
            """,
            new { TableName }) == 1;

    private static Task<int> CountSqlServerClaimsAsync(SqlConnection connection, Guid fileId) =>
        connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(1)
            FROM dbo.{TableName}
            WHERE FileId = @FileId
            """, new { FileId = fileId });

    private static Task<int> CountMySqlClaimsAsync(MySqlConnection connection, Guid fileId) =>
        connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(1)
            FROM {TableName}
            WHERE FileId = @FileId
            """, new { FileId = fileId });

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