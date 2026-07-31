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
/// 只验证 034 在列、外键或索引部分存在时能够收敛，供受影响选择器快速执行。
/// </summary>
[TestClass]
public sealed class Migration034OrganizationPositionUnitRecoveryTests
{
    [TestMethod]
    public async Task MySql_organization_position_unit_migration_recovers_partial_state()
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
            DROP INDEX IX_fn_organization_position_Tenant_Unit
                ON fn_organization_position;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%034_OrganizationPositionUnit.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position'
              AND COLUMN_NAME = 'UnitId'
              AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position'
              AND CONSTRAINT_NAME = 'FK_fn_organization_position_Unit'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT INDEX_NAME)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_organization_position'
              AND INDEX_NAME = 'IX_fn_organization_position_Tenant_Unit'
            """));
    }

    [TestMethod]
    public async Task SqlServer_organization_position_unit_migration_recovers_partial_state()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_organization_position_Tenant_Unit
                ON dbo.fn_organization_position;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%034_OrganizationPositionUnit.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'fn_organization_position'
              AND COLUMN_NAME = 'UnitId'
              AND DATA_TYPE = 'uniqueidentifier'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_position')
              AND name = N'FK_fn_organization_position_Unit'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position')
              AND name = N'IX_fn_organization_position_Tenant_Unit'
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
