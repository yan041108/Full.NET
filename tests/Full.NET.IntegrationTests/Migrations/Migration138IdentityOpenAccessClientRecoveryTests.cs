using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 138 在 OpenAccess 接入方应用表已存在时仍可幂等重跑。</summary>
[TestClass]
public sealed class Migration138IdentityOpenAccessClientRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_open_access_client_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_open_access_client", isSqlServer: true));

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_open_access_client", isSqlServer: true));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_open_access_client_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_open_access_client", isSqlServer: false));

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_open_access_client", isSqlServer: false));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%138_IdentityOpenAccessClient.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%138_IdentityOpenAccessClient.sql';
                  """);

    private static Task<int> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        bool isSqlServer) =>
        connection.ExecuteScalarAsync<int>(
            isSqlServer
                ? """
                  SELECT COUNT(*)
                  FROM sys.tables
                  WHERE name = @TableName
                  """
                : """
                  SELECT COUNT(*)
                  FROM information_schema.tables
                  WHERE table_schema = DATABASE()
                    AND table_name = @TableName
                  """,
            new { TableName = tableName });

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
