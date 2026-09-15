using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 136 在行政区域表已存在时仍可幂等重跑。</summary>
[TestClass]
public sealed class Migration136RegionsAdministrativeRegionRecoveryTests
{
    private const string ParentIdIndex = "IX_fn_regions_administrative_region_ParentId";

    [TestMethod]
    public async Task SqlServer_regions_administrative_region_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_administrative_region", isSqlServer: true));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_dataset_manifest", isSqlServer: true));

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_administrative_region", isSqlServer: true));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task SqlServer_regions_administrative_region_migration_recovers_parent_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             DROP INDEX {ParentIdIndex} ON dbo.fn_regions_administrative_region;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%136_RegionsAdministrativeRegion.sql';
             """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await IndexExistsAsync(connection, isSqlServer: true));
    }

    [TestMethod]
    public async Task MySql_regions_administrative_region_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_administrative_region", isSqlServer: false));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_dataset_manifest", isSqlServer: false));

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_regions_administrative_region", isSqlServer: false));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_regions_administrative_region_migration_recovers_parent_index()
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
             ALTER TABLE fn_regions_administrative_region
                 DROP FOREIGN KEY FK_fn_regions_administrative_region_Parent;
             DROP INDEX {ParentIdIndex} ON fn_regions_administrative_region;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%136_RegionsAdministrativeRegion.sql';
             """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await IndexExistsAsync(connection, isSqlServer: false));
    }

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

    private static Task<int> IndexExistsAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteScalarAsync<int>(
            isSqlServer
                ? """
                  SELECT COUNT(*)
                  FROM sys.indexes
                  WHERE object_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
                    AND name = @IndexName
                  """
                : """
                  SELECT CASE WHEN EXISTS (
                      SELECT 1
                      FROM information_schema.statistics
                      WHERE table_schema = DATABASE()
                        AND table_name = 'fn_regions_administrative_region'
                        AND index_name = @IndexName
                      LIMIT 1) THEN 1 ELSE 0 END
                  """,
            new { IndexName = ParentIdIndex });

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%136_RegionsAdministrativeRegion.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%136_RegionsAdministrativeRegion.sql';
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
