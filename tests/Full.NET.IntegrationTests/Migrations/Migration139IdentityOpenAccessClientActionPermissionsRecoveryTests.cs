using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 139 为全部存量角色幂等补齐 OpenAccess 接入方应用动作权限。</summary>
[TestClass]
public sealed class Migration139IdentityOpenAccessClientActionPermissionsRecoveryTests
{
    private static readonly string[] ActionPermissions =
    [
        IdentityOpenAccessClientPermissions.Read,
        IdentityOpenAccessClientPermissions.Create,
        IdentityOpenAccessClientPermissions.Update,
        IdentityOpenAccessClientPermissions.Disable,
        IdentityOpenAccessClientPermissions.Rotate,
    ];

    [TestMethod]
    public async Task SqlServer_grants_all_open_access_client_permissions_to_existing_roles()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedRoleWithoutOpenAccessClientPermissionsAsync(connection, roleId, now, isSqlServer: true);

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: true, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task SqlServer_deduplicates_existing_open_access_client_permissions()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedRoleWithoutOpenAccessClientPermissionsAsync(connection, roleId, now, isSqlServer: true);
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @PermissionCode);
            """,
            new { RoleId = roleId, PermissionCode = IdentityOpenAccessClientPermissions.Read });

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: true, ActionPermissions);
    }

    [TestMethod]
    public async Task MySql_grants_all_open_access_client_permissions_to_existing_roles()
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
        var now = DateTimeOffset.UtcNow;
        await SeedRoleWithoutOpenAccessClientPermissionsAsync(connection, roleId, now, isSqlServer: false);

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: false, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_deduplicates_existing_open_access_client_permissions()
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
        var now = DateTimeOffset.UtcNow;
        await SeedRoleWithoutOpenAccessClientPermissionsAsync(connection, roleId, now, isSqlServer: false);
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @PermissionCode);
            """,
            new { RoleId = roleId, PermissionCode = IdentityOpenAccessClientPermissions.Read });

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: false, ActionPermissions);
    }

    private static Task SeedRoleWithoutOpenAccessClientPermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid roleId,
        DateTimeOffset now,
        bool isSqlServer)
    {
        var sql = isSqlServer
            ? """
              INSERT INTO dbo.fn_identity_role
                  (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                   IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
              VALUES
                  (@RoleId, NULL, 'host', @RoleCode, N'OpenAccess Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """
            : """
              INSERT INTO fn_identity_role
                  (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                   IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
              VALUES
                  (@RoleId, NULL, 'host', @RoleCode, 'OpenAccess Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """;
        return connection.ExecuteAsync(
            sql,
            new
            {
                RoleId = roleId,
                RoleCode = $"openaccess-seed-{roleId:N}"[..24],
                Now = now,
            });
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%139_IdentityOpenAccessClientActionPermissions.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%139_IdentityOpenAccessClientActionPermissions.sql';
                  """);

    private static async Task AssertRolePermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid roleId,
        bool isSqlServer,
        params string[] expectedPermissions)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_role_permission" : "fn_identity_role_permission";
        var permissions = (await connection.QueryAsync<string>(
            $"""
             SELECT PermissionCode
             FROM {tableName}
             WHERE RoleId = @RoleId
               AND PermissionCode LIKE 'identity.open_access_clients.%'
             ORDER BY PermissionCode
             """,
            new { RoleId = roleId })).ToArray();
        CollectionAssert.AreEquivalent(expectedPermissions, permissions);
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
