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
/// 验证 082 在档案表被误删后可在不破坏用户主数据的前提下恢复表结构、主键与外键约束。
/// </summary>
[TestClass]
public sealed class Migration082IdentityUserProfileRecoveryTests
{
    private const string TableName = "fn_identity_user_profile";
    private const string ForeignKeyName = "FK_fn_identity_user_profile_User";

    [TestMethod]
    public async Task SqlServer_user_profile_migration_recovers_missing_table_without_dropping_user_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await SeedSqlServerUserAsync(connection, userId, now);
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_user_profile
                (UserId, Nickname, PhoneNumber, Version)
            VALUES
                (@UserId, N'恢复前档案', N'13800000001', 1);
            """,
            new { UserId = userId });
        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_identity_user_profile;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%082_IdentityUserProfile.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await CountSqlServerUsersAsync(connection, userId));
        Assert.IsTrue(await SqlServerTableExistsAsync(connection));
        Assert.IsTrue(await SqlServerForeignKeyExistsAsync(connection));
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_user_profile
                (UserId, Nickname, PhoneNumber, Version)
            VALUES
                (@UserId, N'恢复后档案', N'13800000002', 1);
            """,
            new { UserId = userId });
        Assert.AreEqual(
            "恢复后档案",
            await connection.ExecuteScalarAsync<string>(
                """
                SELECT Nickname
                FROM dbo.fn_identity_user_profile
                WHERE UserId = @UserId
                """,
                new { UserId = userId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_user_profile_migration_recovers_missing_table_without_dropping_user_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await SeedMySqlUserAsync(connection, userId, now);
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_user_profile
                (UserId, Nickname, PhoneNumber, Version)
            VALUES
                (@UserId, '恢复前档案', '13800000001', 1);
            """,
            new { UserId = userId });
        await connection.ExecuteAsync(
            """
            DROP TABLE fn_identity_user_profile;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%082_IdentityUserProfile.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await CountMySqlUsersAsync(connection, userId));
        Assert.IsTrue(await MySqlTableExistsAsync(connection));
        Assert.IsTrue(await MySqlForeignKeyExistsAsync(connection));
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_user_profile
                (UserId, Nickname, PhoneNumber, Version)
            VALUES
                (@UserId, '恢复后档案', '13800000002', 1);
            """,
            new { UserId = userId });
        Assert.AreEqual(
            "恢复后档案",
            await connection.ExecuteScalarAsync<string>(
                """
                SELECT Nickname
                FROM fn_identity_user_profile
                WHERE UserId = @UserId
                """,
                new { UserId = userId }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task SeedSqlServerUserAsync(
        SqlConnection connection,
        Guid userId,
        DateTimeOffset now) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, N'档案恢复测试用户',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            """,
            new
            {
                UserId = userId,
                Username = $"profile-{userId:N}"[..24],
                NormalizedUsername = $"PROFILE-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Now = now,
            });

    private static async Task SeedMySqlUserAsync(
        MySqlConnection connection,
        Guid userId,
        DateTimeOffset now) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, '档案恢复测试用户',
                 'unused', 1, 0, NULL, @SecurityStamp, @Now, NULL, 1);
            """,
            new
            {
                UserId = userId,
                Username = $"profile-{userId:N}"[..24],
                NormalizedUsername = $"PROFILE-{userId:N}"[..24],
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Now = now,
            });

    private static Task<int> CountSqlServerUsersAsync(SqlConnection connection, Guid userId) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_identity_user WHERE Id = @UserId",
            new { UserId = userId });

    private static Task<int> CountMySqlUsersAsync(MySqlConnection connection, Guid userId) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_identity_user WHERE Id = @UserId",
            new { UserId = userId });

    private static async Task<bool> SqlServerTableExistsAsync(SqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE name = @TableName
            """,
            new { TableName }) == 1;

    private static async Task<bool> SqlServerForeignKeyExistsAsync(SqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.foreign_keys
            WHERE name = @ForeignKeyName
              AND parent_object_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
            """,
            new { ForeignKeyName }) == 1;

    private static async Task<bool> MySqlTableExistsAsync(MySqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @TableName
            """,
            new { TableName }) == 1;

    private static async Task<bool> MySqlForeignKeyExistsAsync(MySqlConnection connection) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @TableName
              AND CONSTRAINT_NAME = @ForeignKeyName
              AND CONSTRAINT_TYPE = 'FOREIGN KEY'
            """,
            new { TableName, ForeignKeyName }) == 1;

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