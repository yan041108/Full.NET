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
/// 验证 052 将诊断策略权限码从连字符形态迁移为 lower_snake，并安全处理角色重复授权与 API Key JSON 去重。
/// </summary>
[TestClass]
public sealed class Migration052IdentityDiagnosticPolicyPermissionRecoveryTests
{
    private const string LegacyRead = "settings.diagnostic-policy.read";
    private const string LegacyWrite = "settings.diagnostic-policy.write";
    private const string CanonicalRead = "settings.diagnostic_policy.read";
    private const string CanonicalWrite = "settings.diagnostic_policy.write";
    private const string UnrelatedPermission = "identity.users.read";

    [TestMethod]
    public async Task SqlServer_diagnostic_policy_permission_migration_normalizes_role_and_api_key_grants()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var renameRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedSqlServerStateAsync(
            connection,
            renameRoleId,
            userId,
            apiKeyId,
            now,
            insertCanonicalGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyRead}}","{{LegacyWrite}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            renameRoleId,
            isSqlServer: true,
            CanonicalRead,
            CanonicalWrite);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: true));
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: true,
            CanonicalRead,
            CanonicalWrite,
            UnrelatedPermission);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }
    [TestMethod]
    public async Task SqlServer_diagnostic_policy_permission_migration_deduplicates_existing_canonical_grants()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var duplicateRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedSqlServerStateAsync(
            connection,
            duplicateRoleId,
            userId,
            apiKeyId,
            now,
            insertCanonicalGrants: true,
            apiKeyPermissionsJson:
                $$"""["{{LegacyRead}}","{{CanonicalRead}}","{{UnrelatedPermission}}","{{LegacyWrite}}","{{CanonicalWrite}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            duplicateRoleId,
            isSqlServer: true,
            CanonicalRead,
            CanonicalWrite);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: true,
            CanonicalRead,
            CanonicalWrite,
            UnrelatedPermission);
    }

    [TestMethod]
    public async Task SqlServer_diagnostic_policy_permission_migration_preserves_empty_api_key_permissions()
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
            insertCanonicalGrants: false,
            apiKeyPermissionsJson: "[]");

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("[]", await ReadApiKeyPermissionsJsonAsync(connection, apiKeyId, isSqlServer: true));
    }

    [TestMethod]
    public async Task MySql_diagnostic_policy_permission_migration_normalizes_role_and_api_key_grants()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var renameRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedMySqlStateAsync(
            connection,
            renameRoleId,
            userId,
            apiKeyId,
            now,
            insertCanonicalGrants: false,
            apiKeyPermissionsJson:
                $$"""["{{LegacyRead}}","{{LegacyWrite}}","{{UnrelatedPermission}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            renameRoleId,
            isSqlServer: false,
            CanonicalRead,
            CanonicalWrite);
        Assert.AreEqual(0, await CountLegacyRolePermissionsAsync(connection, isSqlServer: false));
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: false,
            CanonicalRead,
            CanonicalWrite,
            UnrelatedPermission);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }
    [TestMethod]
    public async Task MySql_diagnostic_policy_permission_migration_deduplicates_existing_canonical_grants()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var duplicateRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedMySqlStateAsync(
            connection,
            duplicateRoleId,
            userId,
            apiKeyId,
            now,
            insertCanonicalGrants: true,
            apiKeyPermissionsJson:
                $$"""["{{LegacyRead}}","{{CanonicalRead}}","{{UnrelatedPermission}}","{{LegacyWrite}}","{{CanonicalWrite}}"]""");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRolePermissionsAsync(
            connection,
            duplicateRoleId,
            isSqlServer: false,
            CanonicalRead,
            CanonicalWrite);
        await AssertApiKeyPermissionsAsync(
            connection,
            apiKeyId,
            isSqlServer: false,
            CanonicalRead,
            CanonicalWrite,
            UnrelatedPermission);
    }

    [TestMethod]
    public async Task MySql_diagnostic_policy_permission_migration_preserves_empty_api_key_permissions()
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
            insertCanonicalGrants: false,
            apiKeyPermissionsJson: "[]");

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("[]", await ReadApiKeyPermissionsJsonAsync(connection, apiKeyId, isSqlServer: false));
    }
    private static async Task SeedSqlServerStateAsync(
        SqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        bool insertCanonicalGrants,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, N'Diagnostic Legacy', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, N'API User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @LegacyRead), (@RoleId, @LegacyWrite);
            INSERT INTO dbo.fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, N'Legacy Diagnostic Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"diag-legacy-{roleId:N}"[..24],
                UserId = userId,
                Username = $"diag-{userId:N}"[..24],
                NormalizedUsername = $"DIAG-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                LegacyRead,
                LegacyWrite,
                Now = now,
            });

        if (insertCanonicalGrants)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
                VALUES (@RoleId, @CanonicalRead), (@RoleId, @CanonicalWrite);
                """,
                new { RoleId = roleId, CanonicalRead, CanonicalWrite });
        }
    }

    private static async Task SeedMySqlStateAsync(
        MySqlConnection connection,
        Guid roleId,
        Guid userId,
        Guid apiKeyId,
        DateTimeOffset now,
        bool insertCanonicalGrants,
        string apiKeyPermissionsJson)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', @RoleCode, 'Diagnostic Legacy', 0, 1,
                 0, @Now, NULL, 1);
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, 'API User',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
            VALUES (@RoleId, @LegacyRead), (@RoleId, @LegacyWrite);
            INSERT INTO fn_identity_api_key
                (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
                 ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@ApiKeyId, @UserId, 'Legacy Diagnostic Key', @KeyPrefix, @KeyHash,
                 @PermissionsJson, NULL, 1, NULL, NULL, @Now, NULL, 1);
            """,
            new
            {
                RoleId = roleId,
                RoleCode = $"diag-legacy-{roleId:N}"[..24],
                UserId = userId,
                Username = $"diag-{userId:N}"[..24],
                NormalizedUsername = $"DIAG-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ApiKeyId = apiKeyId,
                KeyPrefix = userId.ToString("N")[..12],
                KeyHash = Guid.NewGuid().ToString("N"),
                PermissionsJson = apiKeyPermissionsJson,
                LegacyRead,
                LegacyWrite,
                Now = now,
            });

        if (insertCanonicalGrants)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
                VALUES (@RoleId, @CanonicalRead), (@RoleId, @CanonicalWrite);
                """,
                new { RoleId = roleId, CanonicalRead, CanonicalWrite });
        }
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%052_IdentityDiagnosticPolicyPermission.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%052_IdentityDiagnosticPolicyPermission.sql';
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
             WHERE PermissionCode IN (@LegacyRead, @LegacyWrite)
             """,
            new { LegacyRead, LegacyWrite });
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
