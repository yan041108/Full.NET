using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class MySqlMigrationTests
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0")
        .WithDatabase("fullnet")
        .WithUsername("fullnet")
        .WithPassword("FullNet_Test!123")
        .Build();

    [TestInitialize]
    public Task StartAsync() => _container.StartAsync();

    [TestCleanup]
    public async Task CleanupAsync() => await _container.DisposeAsync();

    [TestMethod]
    public async Task MySql_migration_is_idempotent_and_creates_binary_outbox_schema()
    {
        var runner = new DbUpMigrationRunner(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = _container.GetConnectionString(),
            }),
            NullLoggerFactory.Instance);

        var first = await runner.MigrateAsync();
        var second = await runner.MigrateAsync();

        Assert.IsTrue(first.Successful);
        Assert.IsTrue(first.ExecutedScriptCount > 0);
        Assert.IsTrue(second.Successful);
        Assert.AreEqual(0, second.ExecutedScriptCount);

        await using var connection = new MySqlConnection(_container.GetConnectionString());
        var tables = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE()"))
            .ToArray();

        AssertContainsIgnoreCase(tables, "fn_tenant_tenant");
        AssertContainsIgnoreCase(tables, "fn_outbox_message");
        AssertContainsIgnoreCase(tables, "fn_identity_user");
        AssertContainsIgnoreCase(tables, "fn_identity_refresh_session");
        AssertContainsIgnoreCase(tables, "fn_identity_auth_audit");
        AssertContainsIgnoreCase(tables, "fn_identity_role");
        AssertContainsIgnoreCase(tables, "fn_identity_user_role");
        AssertContainsIgnoreCase(tables, "fn_identity_role_permission");
        AssertContainsIgnoreCase(tables, "SchemaVersions");

        var indexes = (await connection.QueryAsync<string>(
            """
            SELECT DISTINCT INDEX_NAME
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'fn_identity_%'
            """))
            .ToArray();

        AssertRequiredIdentityIndexes(indexes);

        var foreignKeys = (await connection.QueryAsync<string>(
            """
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME LIKE 'fn_identity_%'
            """))
            .ToArray();
        AssertRequiredAuthorizationForeignKeys(foreignKeys);

        var identityColumns = (await connection.QueryAsync<IdentityColumnMetadata>(
            """
            SELECT TABLE_NAME AS TableName,
                   COLUMN_NAME AS Name,
                   IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('fn_identity_refresh_session', 'fn_identity_auth_audit')
            """))
            .ToArray();
        AssertNullableColumn(
            identityColumns,
            "fn_identity_refresh_session",
            "ActiveTenantId");
        AssertNullableColumn(
            identityColumns,
            "fn_identity_auth_audit",
            "ContextTenantId");

        var localizationColumns = (await connection.QueryAsync<LocalizationColumnMetadata>(
            """
            SELECT TABLE_NAME AS TableName,
                   COLUMN_NAME AS Name,
                   IS_NULLABLE AS IsNullable,
                   COLUMN_DEFAULT AS ColumnDefault,
                   CAST(CHARACTER_MAXIMUM_LENGTH AS SIGNED) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND
              (
                  (TABLE_NAME = 'fn_identity_user'
                   AND COLUMN_NAME IN ('PreferredLocale', 'ProfileVersion'))
                  OR (TABLE_NAME = 'fn_tenant_tenant'
                      AND COLUMN_NAME = 'DefaultLocale')
              )
            """))
            .ToArray();
        AssertLocalizationColumns(localizationColumns);

        var columns = (await connection.QueryAsync<ColumnMetadata>(
            """
            SELECT COLUMN_NAME AS Name,
                   DATA_TYPE AS DataType,
                   CAST(CHARACTER_MAXIMUM_LENGTH AS SIGNED) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_outbox_message'
            """))
            .ToArray();

        AssertRequiredOutboxColumns(columns);
        var payload = columns.Single(column =>
            string.Equals(column.Name, "Payload", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("longblob", payload.DataType, ignoreCase: true);
    }

    [TestMethod]
    public async Task MySql_localization_migration_recovers_legacy_and_partial_states()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_identity_user DROP COLUMN PreferredLocale, DROP COLUMN ProfileVersion;
            ALTER TABLE fn_tenant_tenant DROP COLUMN DefaultLocale;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%004_LocalizationPreferences.sql';
            """);
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES
                (@TenantId, 'legacy', 'Legacy', 'legacy.localhost', true, @Now, NULL, 1);
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', 'legacy', 'LEGACY', 'Legacy',
                 'unused', true, 0, NULL, 'stamp', @Now, NULL, 1);
            """,
            new { TenantId = tenantId, UserId = userId, Now = now });

        var legacyUpgrade = await runner.MigrateAsync();
        Assert.AreEqual(1, legacyUpgrade.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);

        // MySQL DDL 会隐式提交；模拟列已存在但约束未收紧、DbUp 尚未记账的恢复路径。
        await connection.ExecuteAsync(
            """
            DELETE FROM schemaversions WHERE ScriptName LIKE '%004_LocalizationPreferences.sql';
            ALTER TABLE fn_identity_user
                MODIFY COLUMN PreferredLocale varchar(35) NULL,
                MODIFY COLUMN ProfileVersion int NULL;
            ALTER TABLE fn_tenant_tenant MODIFY COLUMN DefaultLocale varchar(35) NULL;
            UPDATE fn_identity_user SET PreferredLocale = NULL, ProfileVersion = NULL WHERE Id = @UserId;
            UPDATE fn_tenant_tenant SET DefaultLocale = NULL WHERE Id = @TenantId;
            """,
            new { UserId = userId, TenantId = tenantId });

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);
    }

    private DbUpMigrationRunner CreateRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = _container.GetConnectionString(),
        }),
        NullLoggerFactory.Instance);

    private static async Task AssertLocalizationStateAsync(
        MySqlConnection connection,
        Guid userId,
        Guid tenantId)
    {
        var values = await connection.QuerySingleAsync<LocalizationValues>(
            """
            SELECT identityUser.PreferredLocale,
                   identityUser.ProfileVersion,
                   tenantObject.DefaultLocale
            FROM fn_identity_user AS identityUser
            CROSS JOIN fn_tenant_tenant AS tenantObject
            WHERE identityUser.Id = @UserId AND tenantObject.Id = @TenantId
            """,
            new { UserId = userId, TenantId = tenantId });
        Assert.AreEqual("zh-CN", values.PreferredLocale);
        Assert.AreEqual(1, values.ProfileVersion);
        Assert.AreEqual("zh-CN", values.DefaultLocale);

        var columns = (await connection.QueryAsync<LocalizationColumnMetadata>(
            """
            SELECT TABLE_NAME AS TableName,
                   COLUMN_NAME AS Name,
                   IS_NULLABLE AS IsNullable,
                   COLUMN_DEFAULT AS ColumnDefault,
                   CAST(CHARACTER_MAXIMUM_LENGTH AS SIGNED) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND COLUMN_NAME IN ('PreferredLocale', 'ProfileVersion', 'DefaultLocale')
            """))
            .ToArray();
        AssertLocalizationColumns(columns);
    }

    private static void AssertRequiredOutboxColumns(IEnumerable<ColumnMetadata> columns)
    {
        var names = columns.Select(column => column.Name).ToArray();
        AssertContainsIgnoreCase(names, "SchemaVersion");
        AssertContainsIgnoreCase(names, "ContentType");
        AssertContainsIgnoreCase(names, "TenantId");
        AssertContainsIgnoreCase(names, "TraceId");
        AssertContainsIgnoreCase(names, "Payload");
    }

    private static void AssertRequiredIdentityIndexes(IEnumerable<string> indexes)
    {
        AssertContainsIgnoreCase(indexes, "UX_fn_identity_user_Scope_Username");
        AssertContainsIgnoreCase(indexes, "UX_fn_identity_refresh_session_TokenHash");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_refresh_session_Family");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_refresh_session_User");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_auth_audit_OccurredAt");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_auth_audit_User");
        AssertContainsIgnoreCase(indexes, "UX_fn_identity_role_Scope_Code");
    }

    private static void AssertRequiredAuthorizationForeignKeys(
        IEnumerable<string> foreignKeys)
    {
        AssertContainsIgnoreCase(foreignKeys, "FK_fn_identity_user_role_User");
        AssertContainsIgnoreCase(foreignKeys, "FK_fn_identity_user_role_Role");
        AssertContainsIgnoreCase(foreignKeys, "FK_fn_identity_role_permission_Role");
    }

    private static void AssertNullableColumn(
        IEnumerable<IdentityColumnMetadata> columns,
        string tableName,
        string columnName)
    {
        var column = columns.Single(item =>
            string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("YES", column.IsNullable, ignoreCase: true);
    }

    private static void AssertLocalizationColumns(
        IReadOnlyCollection<LocalizationColumnMetadata> columns)
    {
        Assert.HasCount(3, columns);
        foreach (var name in new[] { "PreferredLocale", "DefaultLocale" })
        {
            var column = columns.Single(item => item.Name == name);
            Assert.AreEqual("NO", column.IsNullable, ignoreCase: true);
            Assert.AreEqual(35L, column.MaximumLength);
            Assert.AreEqual("zh-CN", column.ColumnDefault);
        }

        var version = columns.Single(item => item.Name == "ProfileVersion");
        Assert.AreEqual("NO", version.IsNullable, ignoreCase: true);
        Assert.AreEqual("1", version.ColumnDefault);
    }

    private static void AssertContainsIgnoreCase(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase));

    private sealed record ColumnMetadata(string Name, string DataType, long? MaximumLength);

    private sealed record IdentityColumnMetadata(
        string TableName,
        string Name,
        string IsNullable);

    private sealed record LocalizationColumnMetadata(
        string TableName,
        string Name,
        string IsNullable,
        string ColumnDefault,
        long? MaximumLength);

    private sealed record LocalizationValues(
        string PreferredLocale,
        int ProfileVersion,
        string DefaultLocale);
}
