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
/// 验证 084 在投影表被误删后可在不破坏 Identity 主数据的前提下恢复表结构与主键。
/// </summary>
[TestClass]
public sealed class Migration084IdentityOrganizationUnitProjectionRecoveryTests
{
    private const string TableName = "fn_identity_organization_unit_projection";
    private const string MigrationScriptToken = "084_IdentityOrganizationUnitProjection.sql";

    [TestMethod]
    public async Task SqlServer_organization_unit_projection_migration_recovers_missing_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await SeedSqlServerProjectionAsync(connection, tenantId, unitId, now, "恢复前单位");
        await connection.ExecuteAsync(
            $"""
            DROP TABLE dbo.{TableName};
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await SqlServerTableExistsAsync(connection));
        await SeedSqlServerProjectionAsync(connection, tenantId, unitId, now, "恢复后单位");
        Assert.AreEqual(
            "恢复后单位",
            await connection.ExecuteScalarAsync<string>(
                $"""
                SELECT Name
                FROM dbo.{TableName}
                WHERE TenantId = @TenantId AND UnitId = @UnitId
                """,
                new { TenantId = tenantId, UnitId = unitId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_organization_unit_projection_migration_recovers_missing_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await SeedMySqlProjectionAsync(connection, tenantId, unitId, now, "恢复前单位");
        await connection.ExecuteAsync(
            $"""
            DROP TABLE {TableName};
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await MySqlTableExistsAsync(connection));
        await SeedMySqlProjectionAsync(connection, tenantId, unitId, now, "恢复后单位");
        Assert.AreEqual(
            "恢复后单位",
            await connection.ExecuteScalarAsync<string>(
                $"""
                SELECT Name
                FROM {TableName}
                WHERE TenantId = @TenantId AND UnitId = @UnitId
                """,
                new { TenantId = tenantId, UnitId = unitId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task SeedSqlServerProjectionAsync(
        SqlConnection connection,
        Guid tenantId,
        Guid unitId,
        DateTimeOffset now,
        string name) =>
        connection.ExecuteAsync(
            $"""
            INSERT INTO dbo.{TableName}
                (TenantId, UnitId, Name, IsActive, SourceVersion, SourceUpdatedAtUtc, ProjectedAtUtc)
            VALUES
                (@TenantId, @UnitId, @Name, 1, 1, @Now, @Now);
            """,
            new { TenantId = tenantId, UnitId = unitId, Name = name, Now = now });

    private static Task SeedMySqlProjectionAsync(
        MySqlConnection connection,
        Guid tenantId,
        Guid unitId,
        DateTimeOffset now,
        string name) =>
        connection.ExecuteAsync(
            $"""
            INSERT INTO {TableName}
                (TenantId, UnitId, Name, IsActive, SourceVersion, SourceUpdatedAtUtc, ProjectedAtUtc)
            VALUES
                (@TenantId, @UnitId, @Name, 1, 1, @Now, @Now);
            """,
            new { TenantId = tenantId, UnitId = unitId, Name = name, Now = now });

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
