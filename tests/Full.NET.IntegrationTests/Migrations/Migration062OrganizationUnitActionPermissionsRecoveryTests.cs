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

/// <summary>
/// 验证 062 将存量 organization.units.write 展开为三个精确租户机构动作权限，并安全处理角色重复授权与 API Key JSON 去重。
/// </summary>
[TestClass]
public sealed class Migration062OrganizationUnitActionPermissionsRecoveryTests
{
    private const string LegacyWrite = "organization.units.write";
    private const string ActionCreate = "organization.units.create";
    private const string UnrelatedPermission = "identity.roles.read";

    private static readonly string[] ActionPermissions =
    [
        ActionCreate,
        "organization.units.update",
        "organization.units.disable",
    ];

    [TestMethod]
    public async Task SqlServer_expands_legacy_role_and_api_key_grants()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyWrite}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: true, ActionPermissions);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: true));
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: true,
            [.. ActionPermissions, UnrelatedPermission]);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task SqlServer_deduplicates_existing_action_grants()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyWrite}}","{{ActionCreate}}","{{UnrelatedPermission}}","{{LegacyWrite}}","{{ActionCreate}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: true, ActionPermissions);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: true,
            [.. ActionPermissions, UnrelatedPermission]);
    }

    [TestMethod]
    public async Task SqlServer_partial_expansion_converges_on_rerun()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson: $$"""["{{LegacyWrite}}"]""");
        await InsertRoleActionGrantsAsync(connection, roleId, isSqlServer: true, ActionCreate);

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: true, ActionPermissions);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: true));
        await AssertApiKeyPermissionsAsync(connection, apiKeyId, isSqlServer: true, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_expands_legacy_role_and_api_key_grants()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyWrite}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: false, ActionPermissions);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: false));
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: false,
            [.. ActionPermissions, UnrelatedPermission]);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_deduplicates_existing_action_grants()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyWrite}}","{{ActionCreate}}","{{UnrelatedPermission}}","{{LegacyWrite}}","{{ActionCreate}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: false, ActionPermissions);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: false,
            [.. ActionPermissions, UnrelatedPermission]);
    }

    [TestMethod]
    public async Task MySql_partial_expansion_converges_on_rerun()
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
            insertExistingActionGrants: false,
            apiKeyPermissionsJson: $$"""["{{LegacyWrite}}"]""");
        await InsertRoleActionGrantsAsync(connection, roleId, isSqlServer: false, ActionCreate);

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(connection, roleId, isSqlServer: false, ActionPermissions);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: false));
        await AssertApiKeyPermissionsAsync(connection, apiKeyId, isSqlServer: false, ActionPermissions);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task SeedSqlServerStateAsync(
        SqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        bool insertExistingActionGrants,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, N'Unit Legacy', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, N'API Menu User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @LegacyWrite);
            INSERT INTO dbo.fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, N'Legacy Package Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"pkg-legacy-{roleId:N}"[..24],
                UserId = userId,
                Username = $"ulegacy-{userId:N}"[..24],
                NormalizedUsername = $"ulegacy-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                LegacyWrite,
                Now = now,
            });

        if (insertExistingActionGrants)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
                VALUES (@RoleId, @ActionCreate);
                """,
                new { RoleId = roleId, ActionCreate });
        }
    }

    private static async Task SeedMySqlStateAsync(
        MySqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        bool insertExistingActionGrants,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, 'Unit Legacy', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, 'API Menu User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @LegacyWrite);
            INSERT INTO fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, 'Legacy Package Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"pkg-legacy-{roleId:N}"[..24],
                UserId = userId,
                Username = $"ulegacy-{userId:N}"[..24],
                NormalizedUsername = $"ulegacy-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                LegacyWrite,
                Now = now,
            });

        if (insertExistingActionGrants)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
                VALUES (@RoleId, @ActionCreate);
                """,
                new { RoleId = roleId, ActionCreate });
        }
    }

    private static Task InsertRoleActionGrantsAsync(
        System.Data.Common.DbConnection connection,
        Guid roleId,
        bool isSqlServer,
        params string[] permissionCodes)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_role_permission" : "fn_identity_role_permission";
        var values = string.Join(", ", permissionCodes.Select(code => $"(@RoleId, '{code}')"));
        return connection.ExecuteAsync(
            $"""
             INSERT INTO {tableName} (RoleId, PermissionCode)
             VALUES {values};
             """,
            new { RoleId = roleId });
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%062_OrganizationUnitActionPermissions.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%062_OrganizationUnitActionPermissions.sql';
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

    private static Task<int> CountLegacyRolePermissionsAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_role_permission" : "fn_identity_role_permission";
        return connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM {tableName}
             WHERE PermissionCode = @LegacyWrite
             """,
            new { LegacyWrite });
    }

    private static async Task AssertApiKeyPermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid apiKeyId,
        bool isSqlServer,
        params string[] expectedPermissions)
    {
        var json = await ReadApiKeyPermissionsJsonAsync(connection, apiKeyId, isSqlServer);
        var permissions = JsonSerializer.Deserialize<string[]>(json) ?? [];
        CollectionAssert.AreEquivalent(expectedPermissions, permissions);
    }

    private static Task<string> ReadApiKeyPermissionsJsonAsync(
        System.Data.Common.DbConnection connection,
        Guid apiKeyId,
        bool isSqlServer)
    {
        var tableName = isSqlServer ? "dbo.fn_identity_api_key" : "fn_identity_api_key";
        return connection.ExecuteScalarAsync<string>(
            $"SELECT PermissionsJson FROM {tableName} WHERE Id = @ApiKeyId",
            new { ApiKeyId = apiKeyId })!;
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

