using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 143 为全部存量角色幂等补齐公告收件与阅读统计权限。</summary>
[TestClass]
public sealed class Migration143NotificationsAnnouncementReceiptPermissionsRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_grants_receipt_permissions_to_existing_roles()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedRoleAsync(connection, roleId, now, isSqlServer: true);

        await DeleteMigrationRecordAsync(connection, isSqlServer: true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRoleHasReceiptPermissionsAsync(connection, roleId, isSqlServer: true);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_grants_receipt_permissions_to_existing_roles()
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
        await SeedRoleAsync(connection, roleId, now, isSqlServer: false);

        await DeleteMigrationRecordAsync(connection, isSqlServer: false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertRoleHasReceiptPermissionsAsync(connection, roleId, isSqlServer: false);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task SeedRoleAsync(
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
                  (@RoleId, NULL, 'host', @RoleCode, N'Announcement Receipt Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """
            : """
              INSERT INTO fn_identity_role
                  (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                   IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
              VALUES
                  (@RoleId, NULL, 'host', @RoleCode, 'Announcement Receipt Seed Role', 0, 1,
                   0, @Now, NULL, 1);
              """;
        return connection.ExecuteAsync(sql, new
        {
            RoleId = roleId,
            RoleCode = $"receipt-seed-{roleId:N}"[..32],
            Now = now,
        });
    }

    private static async Task AssertRoleHasReceiptPermissionsAsync(
        System.Data.Common.DbConnection connection,
        Guid roleId,
        bool isSqlServer)
    {
        var table = isSqlServer ? "dbo.fn_identity_role_permission" : "fn_identity_role_permission";
        foreach (var permission in new[]
                 {
                     HostAnnouncementPermissions.ReceivedRead,
                     HostAnnouncementPermissions.ReceivedMarkRead,
                     HostAnnouncementPermissions.ReceivedMarkAllRead,
                     HostAnnouncementPermissions.ReadStats,
                 })
        {
            var count = await connection.ExecuteScalarAsync<int>(
                $"""
                 SELECT COUNT(1)
                 FROM {table}
                 WHERE RoleId = @RoleId AND PermissionCode = @PermissionCode
                 """,
                new { RoleId = roleId, PermissionCode = permission });
            Assert.AreEqual(1, count, $"Missing permission {permission}");
        }
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%143_NotificationsAnnouncementReceiptPermissions.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%143_NotificationsAnnouncementReceiptPermissions.sql';
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
