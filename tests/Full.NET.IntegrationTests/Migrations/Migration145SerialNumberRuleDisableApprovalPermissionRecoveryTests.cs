using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 145 为已具备 disable 权限的角色与 API Key 幂等授予 submit_disable_approval 权限。</summary>
[TestClass]
public sealed class Migration145SerialNumberRuleDisableApprovalPermissionRecoveryTests
{
    private const string DisablePermission = "serial_numbers.rules.disable";
    private const string SubmitDisablePermission = "serial_numbers.rules.submit_disable_approval";
    private const string UnrelatedPermission = "identity.roles.read";

    [TestMethod]
    public async Task SqlServer_grants_submit_disable_approval_to_disable_role_and_api_key()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedSqlServerStateAsync(
            connection,
            roleId,
            userId,
            apiKeyId,
            now,
            apiKeyPermissionsJson: $$"""["{{DisablePermission}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            roleId,
            isSqlServer: true,
            DisablePermission,
            SubmitDisablePermission);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: true,
            DisablePermission,
            SubmitDisablePermission,
            UnrelatedPermission);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_grants_submit_disable_approval_to_disable_role_and_api_key()
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
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedMySqlStateAsync(
            connection,
            roleId,
            userId,
            apiKeyId,
            now,
            apiKeyPermissionsJson: $$"""["{{DisablePermission}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            roleId,
            isSqlServer: false,
            DisablePermission,
            SubmitDisablePermission);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: false,
            DisablePermission,
            SubmitDisablePermission,
            UnrelatedPermission);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task SeedSqlServerStateAsync(
        SqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, N'Unit Disable', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, N'Disable User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @DisablePermission);
            INSERT INTO dbo.fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, N'Disable Package Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"pkg-disable-{roleId:N}"[..24],
                UserId = userId,
                Username = $"udisable-{userId:N}"[..24],
                NormalizedUsername = $"udisable-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                DisablePermission,
                Now = now,
            });
    }

    private static async Task SeedMySqlStateAsync(
        MySqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, 'Unit Disable', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, 'Disable User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @DisablePermission);
            INSERT INTO fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, 'Disable Package Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"pkg-disable-{roleId:N}"[..24],
                UserId = userId,
                Username = $"udisable-{userId:N}"[..24],
                NormalizedUsername = $"udisable-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                DisablePermission,
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
                  WHERE ScriptName LIKE '%145_SerialNumberRuleDisableApprovalPermission.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%145_SerialNumberRuleDisableApprovalPermission.sql';
                  """);

    private static async Task AssertRolePermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid roleId,
        bool isSqlServer,
        params string[] expectedPermissions)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_role_permission" : "fn_identity_role_permission";
        var permissions = (await connection.QueryAsync<string>(
            $"SELECT PermissionCode FROM {tableName} WHERE RoleId = @RoleId ORDER BY PermissionCode",
            new { RoleId = roleId })).ToArray();
        CollectionAssert.AreEquivalent(expectedPermissions, permissions);
    }

    private static async Task AssertApiKeyPermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid apiKeyId,
        bool isSqlServer,
        params string[] expectedPermissions)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_api_key" : "fn_identity_api_key";
        var json = await connection.ExecuteScalarAsync<string>(
            $"SELECT PermissionsJson FROM {tableName} WHERE Id = @ApiKeyId",
            new { ApiKeyId = apiKeyId });
        var permissions = JsonSerializer.Deserialize<string[]>(json!) ?? [];
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
