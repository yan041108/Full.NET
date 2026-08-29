using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 101 能规范历史资料并从唯一索引缺失状态恢复。</summary>
[TestClass]
public sealed class Migration101IdentityHostUserProfileAuthorityRecoveryTests
{
    private const string PhoneIndex = "UX_fn_identity_user_profile_PhoneNumber";
    private static readonly IReadOnlyDictionary<string, string> ExpectedIndexes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PhoneIndex] = "PhoneNumber",
            ["UX_fn_identity_user_profile_Email"] = "Email",
            ["UX_fn_identity_user_profile_EmployeeNumber"] = "EmployeeNumber",
            ["UX_fn_identity_user_profile_IdCardType_IdCardNumber"] = "IdCardType,IdCardNumber",
        };

    [TestMethod]
    public async Task SqlServer_recovers_profile_authority_index_and_normalizes_values()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        var userId = await SeedSqlServerProfileAsync(connection, " 13800000000 ", " User@Example.COM ");
        var duplicateUserId = await SeedSqlServerProfileAsync(
            connection,
            "13900000000",
            "user@example.com");
        await connection.ExecuteAsync(
            $"""
            DROP INDEX {PhoneIndex} ON dbo.fn_identity_user_profile;
            CREATE INDEX {PhoneIndex} ON dbo.fn_identity_user_profile(Email);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%101_IdentityHostUserProfileAuthority.sql';
            """);

        await AssertMigrationFailsAsync(runner);
        await connection.ExecuteAsync(
            "UPDATE dbo.fn_identity_user_profile SET Email = N'other@example.com' WHERE UserId = @UserId",
            new { UserId = duplicateUserId });
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        foreach (var expectedIndex in ExpectedIndexes)
        {
            Assert.AreEqual(
                expectedIndex.Value,
                await ReadSqlServerIndexColumnsAsync(connection, expectedIndex.Key),
                expectedIndex.Key);
        }
        var profile = await connection.QuerySingleAsync<ProfileAuthorityRow>(
            "SELECT PhoneNumber, Email FROM dbo.fn_identity_user_profile WHERE UserId = @UserId",
            new { UserId = userId });
        Assert.AreEqual("13800000000", profile.PhoneNumber);
        Assert.AreEqual("user@example.com", profile.Email);
        Assert.AreEqual(
            "Latin1_General_100_BIN2",
            await connection.ExecuteScalarAsync<string>(
                "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile') AND name = N'Email'"));
        await Assert.ThrowsAsync<SqlException>(() =>
            SeedSqlServerProfileAsync(connection, profile.PhoneNumber, "other@example.com"));
    }

    [TestMethod]
    public async Task MySql_recovers_profile_authority_index_and_normalizes_values()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var userId = await SeedMySqlProfileAsync(connection, " 13800000000 ", " User@Example.COM ");
        var duplicateUserId = await SeedMySqlProfileAsync(
            connection,
            "13900000000",
            "user@example.com");
        await connection.ExecuteAsync(
            $"""
            DROP INDEX {PhoneIndex} ON fn_identity_user_profile;
            CREATE INDEX {PhoneIndex} ON fn_identity_user_profile(Email);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%101_IdentityHostUserProfileAuthority.sql';
            """);

        await AssertMigrationFailsAsync(runner);
        await connection.ExecuteAsync(
            "UPDATE fn_identity_user_profile SET Email = 'other@example.com' WHERE UserId = @UserId",
            new { UserId = duplicateUserId });
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        foreach (var expectedIndex in ExpectedIndexes)
        {
            Assert.AreEqual(
                expectedIndex.Value,
                await ReadMySqlIndexColumnsAsync(connection, expectedIndex.Key),
                expectedIndex.Key);
        }
        var profile = await connection.QuerySingleAsync<ProfileAuthorityRow>(
            "SELECT PhoneNumber, Email FROM fn_identity_user_profile WHERE UserId = @UserId",
            new { UserId = userId });
        Assert.AreEqual("13800000000", profile.PhoneNumber);
        Assert.AreEqual("user@example.com", profile.Email);
        Assert.AreEqual(
            "utf8mb4_bin",
            await connection.ExecuteScalarAsync<string>(
                "SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND COLUMN_NAME = 'Email'"));
        await Assert.ThrowsAsync<MySqlException>(() =>
            SeedMySqlProfileAsync(connection, profile.PhoneNumber, "other@example.com"));
    }

    private static async Task<Guid> SeedSqlServerProfileAsync(
        SqlConnection connection,
        string phoneNumber,
        string email)
    {
        var userId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, N'迁移测试用户',
                 'unused', 1, 0, NULL, @SecurityStamp, SYSUTCDATETIME(), NULL, 1);
            INSERT INTO dbo.fn_identity_user_profile
                (UserId, PhoneNumber, Email, SortOrder, Version)
            VALUES (@UserId, @PhoneNumber, @Email, 100, 1);
            """,
            CreateSeedParameters(userId, phoneNumber, email));
        return userId;
    }

    private static async Task<Guid> SeedMySqlProfileAsync(
        MySqlConnection connection,
        string phoneNumber,
        string email)
    {
        var userId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', @Username, @NormalizedUsername, '迁移测试用户',
                 'unused', 1, 0, NULL, @SecurityStamp, UTC_TIMESTAMP(6), NULL, 1);
            INSERT INTO fn_identity_user_profile
                (UserId, PhoneNumber, Email, SortOrder, Version)
            VALUES (@UserId, @PhoneNumber, @Email, 100, 1);
            """,
            CreateSeedParameters(userId, phoneNumber, email));
        return userId;
    }

    private static object CreateSeedParameters(Guid userId, string phoneNumber, string email) =>
        new
        {
            UserId = userId,
            Username = $"profile-{userId:N}"[..24],
            NormalizedUsername = $"PROFILE-{userId:N}"[..24],
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PhoneNumber = phoneNumber,
            Email = email,
        };

    private static Task<string?> ReadSqlServerIndexColumnsAsync(
        SqlConnection connection,
        string indexName) =>
        connection.ExecuteScalarAsync<string?>(
            """
            SELECT STRING_AGG(columnObject.name, ',')
                WITHIN GROUP (ORDER BY indexColumn.key_ordinal)
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
               AND indexColumn.key_ordinal > 0
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
              AND indexObject.name = @IndexName
              AND indexObject.is_unique = 1
              AND indexObject.has_filter = 1
            """,
            new { IndexName = indexName });

    private static Task<string?> ReadMySqlIndexColumnsAsync(
        MySqlConnection connection,
        string indexName) =>
        connection.ExecuteScalarAsync<string?>(
            """
            SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ',')
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_identity_user_profile'
              AND INDEX_NAME = @IndexName
              AND NON_UNIQUE = 0
            """,
            new { IndexName = indexName });

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

    private static async Task AssertMigrationFailsAsync(DbUpMigrationRunner runner)
    {
        try
        {
            await runner.MigrateAsync();
            Assert.Fail("规范化产生重复值时迁移必须失败关闭。");
        }
        catch (AssertFailedException)
        {
            throw;
        }
        catch (Exception)
        {
            // 失败类型由数据库提供程序包装；此处只验证迁移未越过重复数据门禁。
        }
    }

    private sealed record ProfileAuthorityRow(string PhoneNumber, string Email);
}
