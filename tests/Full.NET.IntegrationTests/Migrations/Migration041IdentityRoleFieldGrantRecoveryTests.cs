using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 041 能恢复角色、资源和字段三列唯一索引的缺失或错误形状。</summary>
[TestClass]
public sealed class Migration041IdentityRoleFieldGrantRecoveryTests
{
    private const string IndexName =
        "UX_fn_identity_role_field_grant_RoleResourceField";

    [TestMethod]
    public async Task MySql_role_field_grant_migration_recovers_malformed_unique_index()
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
             DROP INDEX {IndexName} ON fn_identity_role_field_grant;
             CREATE UNIQUE INDEX {IndexName}
                 ON fn_identity_role_field_grant(RoleId, ResourceKey, FieldKey, CreatedAtUtc);
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%041_IdentityRoleFieldGrant.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(3, await CountMySqlTotalIndexColumnsAsync(connection));
        Assert.AreEqual(3, await CountMySqlIndexColumnsAsync(connection));
    }

    [TestMethod]
    public async Task SqlServer_role_field_grant_migration_recovers_malformed_unique_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON dbo.fn_identity_role_field_grant;
             CREATE UNIQUE INDEX {IndexName}
                 ON dbo.fn_identity_role_field_grant(RoleId, ResourceKey)
                 WHERE FieldKey <> '';
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%041_IdentityRoleFieldGrant.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(3, await CountSqlServerIndexColumnsAsync(connection));
    }

    private static Task<int> CountMySqlIndexColumnsAsync(MySqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_identity_role_field_grant'
               AND INDEX_NAME = '{IndexName}'
               AND NON_UNIQUE = 0
               AND SUB_PART IS NULL
               AND ((SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'RoleId')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'ResourceKey')
                    OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'FieldKey'))
             """);

    private static Task<int> CountMySqlTotalIndexColumnsAsync(MySqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_identity_role_field_grant'
               AND INDEX_NAME = '{IndexName}'
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
                   OBJECT_ID(N'dbo.fn_identity_role_field_grant')
               AND indexObject.name = N'{IndexName}'
               AND indexObject.is_unique = 1
               AND indexObject.has_filter = 0
               AND indexObject.is_disabled = 0
               AND ((indexColumn.key_ordinal = 1 AND columnObject.name = N'RoleId')
                    OR (indexColumn.key_ordinal = 2 AND columnObject.name = N'ResourceKey')
                    OR (indexColumn.key_ordinal = 3 AND columnObject.name = N'FieldKey'))
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
