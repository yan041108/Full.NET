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
/// 验证 083 可安全移除角色数据范围表的跨模块外键，并允许保留孤儿 UnitId 引用。
/// </summary>
[TestClass]
public sealed class Migration083IdentityRoleDataScopeUnitCrossModuleFkRecoveryTests
{
    private const string ScopeTableName = "fn_identity_role_data_scope_unit";
    private const string OrganizationForeignKeyName = "FK_fn_identity_role_data_scope_unit_Unit";
    private const string RoleForeignKeyName = "FK_fn_identity_role_data_scope_unit_Role";
    private const string MigrationScriptToken = "083_IdentityRoleDataScopeUnitCrossModuleFk.sql";

    [TestMethod]
    public async Task SqlServer_role_data_scope_unit_migration_drops_cross_module_fk_and_recovers()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var orphanUnitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedSqlServerRoleAsync(connection, roleId, now);

        Assert.IsFalse(await SqlServerOrganizationForeignKeyExistsAsync(connection));
        Assert.IsTrue(await SqlServerRoleForeignKeyExistsAsync(connection));

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role_data_scope_unit (RoleId, UnitId)
            VALUES (@RoleId, @UnitId);
            """,
            new { RoleId = roleId, UnitId = orphanUnitId });
        Assert.AreEqual(1, await CountSqlServerScopeRowsAsync(connection, roleId));

        await connection.ExecuteAsync(
            "DELETE FROM dbo.fn_identity_role_data_scope_unit WHERE RoleId = @RoleId",
            new { RoleId = roleId });
        await connection.ExecuteAsync(
            $"""
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_identity_role_data_scope_unit
                ADD CONSTRAINT FK_fn_identity_role_data_scope_unit_Unit
                FOREIGN KEY (UnitId) REFERENCES dbo.fn_organization_unit(Id);
            """);
        Assert.IsTrue(await SqlServerOrganizationForeignKeyExistsAsync(connection));

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsFalse(await SqlServerOrganizationForeignKeyExistsAsync(connection));
        Assert.IsTrue(await SqlServerRoleForeignKeyExistsAsync(connection));
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role_data_scope_unit (RoleId, UnitId)
            VALUES (@RoleId, @UnitId);
            """,
            new { RoleId = roleId, UnitId = orphanUnitId });
        Assert.AreEqual(1, await CountSqlServerScopeRowsAsync(connection, roleId));

        await connection.ExecuteAsync(
            $"""
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);
        Assert.IsFalse(await SqlServerOrganizationForeignKeyExistsAsync(connection));

        var missingConstraint = await runner.MigrateAsync();
        Assert.AreEqual(1, missingConstraint.ExecutedScriptCount);
        Assert.IsFalse(await SqlServerOrganizationForeignKeyExistsAsync(connection));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_role_data_scope_unit_migration_drops_cross_module_fk_and_recovers()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var roleId = Guid.NewGuid();
        var orphanUnitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedMySqlRoleAsync(connection, roleId, now);

        Assert.IsFalse(await MySqlOrganizationForeignKeyExistsAsync(connection));
        Assert.IsTrue(await MySqlRoleForeignKeyExistsAsync(connection));

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role_data_scope_unit (RoleId, UnitId)
            VALUES (@RoleId, @UnitId);
            """,
            new { RoleId = roleId, UnitId = orphanUnitId });
        Assert.AreEqual(1, await CountMySqlScopeRowsAsync(connection, roleId));

        await connection.ExecuteAsync(
            "DELETE FROM fn_identity_role_data_scope_unit WHERE RoleId = @RoleId",
            new { RoleId = roleId });
        await connection.ExecuteAsync(
            $"""
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_identity_role_data_scope_unit
                ADD CONSTRAINT FK_fn_identity_role_data_scope_unit_Unit
                FOREIGN KEY (UnitId) REFERENCES fn_organization_unit(Id);
            """);
        Assert.IsTrue(await MySqlOrganizationForeignKeyExistsAsync(connection));

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsFalse(await MySqlOrganizationForeignKeyExistsAsync(connection));
        Assert.IsTrue(await MySqlRoleForeignKeyExistsAsync(connection));
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role_data_scope_unit (RoleId, UnitId)
            VALUES (@RoleId, @UnitId);
            """,
            new { RoleId = roleId, UnitId = orphanUnitId });
        Assert.AreEqual(1, await CountMySqlScopeRowsAsync(connection, roleId));

        await connection.ExecuteAsync(
            $"""
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%{MigrationScriptToken}';
            """);
        Assert.IsFalse(await MySqlOrganizationForeignKeyExistsAsync(connection));

        var missingConstraint = await runner.MigrateAsync();
        Assert.AreEqual(1, missingConstraint.ExecutedScriptCount);
        Assert.IsFalse(await MySqlOrganizationForeignKeyExistsAsync(connection));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task SeedSqlServerRoleAsync(
        SqlConnection connection,
        Guid roleId,
        DateTimeOffset now) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @Code, N'数据范围外键恢复', 0, 1,
                 0, 'identity.data_scope.custom', @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                Code = $"scope-{roleId:N}"[..24],
                Now = now,
            });

    private static async Task SeedMySqlRoleAsync(
        MySqlConnection connection,
        Guid roleId,
        DateTimeOffset now) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @Code, '数据范围外键恢复', 0, 1,
                 0, 'identity.data_scope.custom', @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                Code = $"scope-{roleId:N}"[..24],
                Now = now,
            });

    private static Task<int> CountSqlServerScopeRowsAsync(SqlConnection connection, Guid roleId) =>
        connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_identity_role_data_scope_unit
            WHERE RoleId = @RoleId
            """,
            new { RoleId = roleId });

    private static Task<int> CountMySqlScopeRowsAsync(MySqlConnection connection, Guid roleId) =>
        connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_identity_role_data_scope_unit
            WHERE RoleId = @RoleId
            """,
            new { RoleId = roleId });

    private static Task<bool> SqlServerOrganizationForeignKeyExistsAsync(SqlConnection connection) =>
        ForeignKeyExistsAsync(connection, OrganizationForeignKeyName);

    private static Task<bool> SqlServerRoleForeignKeyExistsAsync(SqlConnection connection) =>
        ForeignKeyExistsAsync(connection, RoleForeignKeyName);

    private static async Task<bool> ForeignKeyExistsAsync(SqlConnection connection, string foreignKeyName) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.foreign_keys
            WHERE name = @ForeignKeyName
              AND parent_object_id = OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit')
            """,
            new { ForeignKeyName = foreignKeyName }) == 1;

    private static async Task<bool> MySqlOrganizationForeignKeyExistsAsync(MySqlConnection connection) =>
        await MySqlForeignKeyExistsAsync(connection, OrganizationForeignKeyName);

    private static async Task<bool> MySqlRoleForeignKeyExistsAsync(MySqlConnection connection) =>
        await MySqlForeignKeyExistsAsync(connection, RoleForeignKeyName);

    private static async Task<bool> MySqlForeignKeyExistsAsync(MySqlConnection connection, string foreignKeyName) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @ScopeTableName
              AND CONSTRAINT_NAME = @ForeignKeyName
              AND CONSTRAINT_TYPE = 'FOREIGN KEY'
            """,
            new { ScopeTableName, ForeignKeyName = foreignKeyName }) == 1;

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