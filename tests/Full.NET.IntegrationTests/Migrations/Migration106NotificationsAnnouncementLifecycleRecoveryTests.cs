using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 106 扩展公告生命周期列与受众子表的双库幂等恢复。</summary>
[TestClass]
public sealed class Migration106NotificationsAnnouncementLifecycleRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_recovers_dropped_target_tables_and_preserves_announcement_rows()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);

        var announcementId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_announcement
                (Id, TenantId, Title, Content, Kind, AudienceKind, Status, PublishedAtUtc,
                 PublishedByUserId, RetractedAtUtc, RetractedByUserId,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, N'恢复测试', N'正文', 'announcement', 'all', 'draft', NULL,
                 NULL, NULL, NULL,
                 @Now, NULL, @CreatedByUserId, NULL, 1)
            """,
            new { Id = announcementId, Now = now, CreatedByUserId = createdByUserId });

        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_notifications_announcement_target_organization;
            DROP TABLE dbo.fn_notifications_announcement_target_user;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%106_NotificationsAnnouncementLifecycle.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await CountSqlServerTargetTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.fn_notifications_announcement WHERE Id = @Id",
                new { Id = announcementId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_recovers_dropped_target_tables_and_preserves_announcement_rows()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));

        var announcementId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_announcement
                (Id, TenantId, Title, Content, Kind, AudienceKind, Status, PublishedAtUtc,
                 PublishedByUserId, RetractedAtUtc, RetractedByUserId,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, '恢复测试', '正文', 'announcement', 'all', 'draft', NULL,
                 NULL, NULL, NULL,
                 @Now, NULL, @CreatedByUserId, NULL, 1)
            """,
            new { Id = announcementId, Now = now, CreatedByUserId = createdByUserId });

        await connection.ExecuteAsync(
            """
            DROP TABLE fn_notifications_announcement_target_organization;
            DROP TABLE fn_notifications_announcement_target_user;
            DELETE FROM SchemaVersions
            WHERE ScriptName LIKE '%106_NotificationsAnnouncementLifecycle.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await CountMySqlTargetTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_notifications_announcement WHERE Id = @Id",
                new { Id = announcementId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task<int> CountSqlServerTargetTablesAsync(SqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME IN (
                'fn_notifications_announcement_target_user',
                'fn_notifications_announcement_target_organization')
            """);

    private static async Task<int> CountMySqlTargetTablesAsync(MySqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN (
                'fn_notifications_announcement_target_user',
                'fn_notifications_announcement_target_organization')
            """);

    private static DbUpMigrationRunner CreateRunner(DatabaseProvider provider, string connectionString) =>
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
