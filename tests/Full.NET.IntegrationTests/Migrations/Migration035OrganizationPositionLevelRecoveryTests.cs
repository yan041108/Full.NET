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
/// 只验证 035 在职级表已存在但索引缺失时能够收敛，供受影响选择器快速执行。
/// </summary>
[TestClass]
public sealed class Migration035OrganizationPositionLevelRecoveryTests
{
    [TestMethod]
    public async Task MySql_organization_position_level_migration_recovers_partial_state()
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
            """
            DROP INDEX UX_fn_organization_position_level_Tenant_Code
                ON fn_organization_position_level;
            DROP INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
                ON fn_organization_position_level;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%035_OrganizationPositionLevel.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position_level'
              AND COLUMN_NAME = 'Id'
              AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT INDEX_NAME)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position_level'
              AND INDEX_NAME = 'UX_fn_organization_position_level_Tenant_Code'
              AND NON_UNIQUE = 0
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT INDEX_NAME)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position_level'
              AND INDEX_NAME = 'IX_fn_organization_position_level_Tenant_DisplayOrder'
            """));
    }

    [TestMethod]
    public async Task SqlServer_organization_position_level_migration_recovers_partial_state()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_organization_position_level_Tenant_Code
                ON dbo.fn_organization_position_level;
            DROP INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
                ON dbo.fn_organization_position_level;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%035_OrganizationPositionLevel.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'fn_organization_position_level'
              AND COLUMN_NAME = 'Id'
              AND DATA_TYPE = 'uniqueidentifier'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
              AND name = N'UX_fn_organization_position_level_Tenant_Code'
              AND is_unique = 1
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
              AND name = N'IX_fn_organization_position_level_Tenant_DisplayOrder'
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
