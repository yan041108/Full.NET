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

/// <summary>验证 148 为全部存量角色幂等补齐注册策略与注册方式动作权限。</summary>
[TestClass]
public sealed class Migration148IdentityRegistrationActionPermissionsRecoveryTests
{
    private static readonly string[] ActionPermissions =
    [
        IdentityRegistrationPolicyPermissions.Read,
        IdentityRegistrationPolicyPermissions.Update,
        IdentityRegistrationWayPermissions.Read,
        IdentityRegistrationWayPermissions.Create,
        IdentityRegistrationWayPermissions.Update,
        IdentityRegistrationWayPermissions.Delete,
    ];

    [TestMethod]
    public async Task SqlServer_grants_all_registration_permissions_to_existing_roles()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedRoleWithoutRegistrationPermissionsAsync(connection, roleId, now, true);

        await DeleteMigrationRecordAsync(connection, true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, true, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_grants_all_registration_permissions_to_existing_roles()
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
        await SeedRoleWithoutRegistrationPermissionsAsync(connection, roleId, now, false);

        await DeleteMigrationRecordAsync(connection, false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, false, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task SeedRoleWithoutRegistrationPermissionsAsync(
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
                  (@RoleId, NULL, 'host', @RoleCode, N'Registration Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """
            : """
              INSERT INTO fn_identity_role
                  (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                   IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
              VALUES
                  (@RoleId, NULL, 'host', @RoleCode, 'Registration Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """;
        return connection.ExecuteAsync(
            sql,
            new
            {
                RoleId = roleId,
                RoleCode = $"registration-seed-{roleId:N}"[..24],
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
                  WHERE ScriptName LIKE '%148_IdentityRegistrationActionPermissions.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%148_IdentityRegistrationActionPermissions.sql';
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
               AND (
                    PermissionCode LIKE 'identity.registration_policy.%'
                 OR PermissionCode LIKE 'identity.registration_ways.%'
               )
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
