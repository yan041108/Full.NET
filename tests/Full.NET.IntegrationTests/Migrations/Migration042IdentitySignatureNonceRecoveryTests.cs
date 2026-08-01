using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 042 能恢复签名 Nonce 唯一索引的缺失或错误形状。</summary>
[TestClass]
public sealed class Migration042IdentitySignatureNonceRecoveryTests
{
    private const string IndexName = "UX_fn_identity_signature_nonce_AccessKeyNonce";

    [TestMethod]
    public async Task MySql_signature_nonce_migration_recovers_malformed_unique_index()
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
             DROP INDEX {IndexName} ON fn_identity_signature_nonce;
             CREATE UNIQUE INDEX {IndexName}
                 ON fn_identity_signature_nonce(AccessKeyId, NonceDigest, CreatedAtUtc);
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%042_IdentitySignatureNonce.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await CountMySqlIndexColumnsAsync(connection));
    }

    [TestMethod]
    public async Task SqlServer_signature_nonce_migration_recovers_malformed_unique_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON dbo.fn_identity_signature_nonce;
             CREATE UNIQUE INDEX {IndexName}
                 ON dbo.fn_identity_signature_nonce(AccessKeyId)
                 WHERE NonceDigest <> '';
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%042_IdentitySignatureNonce.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await CountSqlServerIndexColumnsAsync(connection));
    }

    private static Task<int> CountMySqlIndexColumnsAsync(MySqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_identity_signature_nonce'
               AND INDEX_NAME = '{IndexName}'
               AND NON_UNIQUE = 0
               AND ((SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'AccessKeyId')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'NonceDigest'))
             """);

    private static Task<int> CountSqlServerIndexColumnsAsync(SqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
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
                   OBJECT_ID(N'dbo.fn_identity_signature_nonce')
               AND indexObject.name = N'{IndexName}'
               AND indexObject.is_unique = 1
               AND indexObject.has_filter = 0
               AND indexObject.is_disabled = 0
               AND ((indexColumn.key_ordinal = 1 AND columnObject.name = N'AccessKeyId')
                    OR (indexColumn.key_ordinal = 2 AND columnObject.name = N'NonceDigest'))
             """);

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
