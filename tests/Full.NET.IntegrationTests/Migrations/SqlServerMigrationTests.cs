using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class SqlServerMigrationTests
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .WithPassword("FullNet_Test!123")
        .Build();

    [TestInitialize]
    public Task StartAsync() => _container.StartAsync();

    [TestCleanup]
    public async Task CleanupAsync() => await _container.DisposeAsync();

    [TestMethod]
    public async Task SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema()
    {
        var runner = new DbUpMigrationRunner(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = _container.GetConnectionString(),
            }),
            NullLoggerFactory.Instance);

        var first = await runner.MigrateAsync();
        var second = await runner.MigrateAsync();

        Assert.IsTrue(first.Successful);
        Assert.IsTrue(first.ExecutedScriptCount > 0);
        Assert.IsTrue(second.Successful);
        Assert.AreEqual(0, second.ExecutedScriptCount);

        await using var connection = new SqlConnection(_container.GetConnectionString());
        var tables = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo'"))
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
            SELECT indexObject.name
            FROM sys.indexes AS indexObject
            INNER JOIN sys.tables AS tableObject ON tableObject.object_id = indexObject.object_id
            WHERE tableObject.name LIKE 'fn_identity_%' AND indexObject.name IS NOT NULL
            """))
            .ToArray();

        AssertRequiredIdentityIndexes(indexes);

        var foreignKeys = (await connection.QueryAsync<string>(
            "SELECT name FROM sys.foreign_keys WHERE name LIKE 'FK_fn_identity_%'"))
            .ToArray();
        AssertRequiredAuthorizationForeignKeys(foreignKeys);

        var identityColumns = (await connection.QueryAsync<IdentityColumnMetadata>(
            """
            SELECT TABLE_NAME AS TableName,
                   COLUMN_NAME AS Name,
                   IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
                   CAST(CHARACTER_MAXIMUM_LENGTH AS bigint) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
                   CAST(CHARACTER_MAXIMUM_LENGTH AS bigint) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'fn_outbox_message'
            """))
            .ToArray();

        AssertRequiredOutboxColumns(columns);
        var payload = columns.Single(column =>
            string.Equals(column.Name, "Payload", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("varbinary", payload.DataType, ignoreCase: true);
        Assert.AreEqual(-1L, payload.MaximumLength);
    }

    [TestMethod]
    public async Task SqlServer_localization_migration_recovers_legacy_and_partial_states()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT DF_fn_identity_user_PreferredLocale;
            ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT DF_fn_identity_user_ProfileVersion;
            ALTER TABLE dbo.fn_tenant_tenant DROP CONSTRAINT DF_fn_tenant_tenant_DefaultLocale;
            ALTER TABLE dbo.fn_identity_user DROP COLUMN PreferredLocale, ProfileVersion;
            ALTER TABLE dbo.fn_tenant_tenant DROP COLUMN DefaultLocale;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%004_LocalizationPreferences.sql';
            """);
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES
                (@TenantId, 'legacy', 'Legacy', 'legacy.localhost', 1, @Now, NULL, 1);
            INSERT INTO dbo.fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
                 SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@UserId, NULL, 'host', 'legacy', 'LEGACY', 'Legacy',
                 'unused', 1, 0, NULL, 'stamp', @Now, NULL, 1);
            """,
            new { TenantId = tenantId, UserId = userId, Now = now });

        var legacyUpgrade = await runner.MigrateAsync();
        Assert.AreEqual(1, legacyUpgrade.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);

        // 模拟 DDL 已部分提交但 DbUp 尚未记账；重跑必须修复空值、可空性和默认约束。
        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%004_LocalizationPreferences.sql';
            ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT DF_fn_identity_user_PreferredLocale;
            ALTER TABLE dbo.fn_identity_user DROP CONSTRAINT DF_fn_identity_user_ProfileVersion;
            ALTER TABLE dbo.fn_tenant_tenant DROP CONSTRAINT DF_fn_tenant_tenant_DefaultLocale;
            ALTER TABLE dbo.fn_identity_user ALTER COLUMN PreferredLocale varchar(35) NULL;
            ALTER TABLE dbo.fn_identity_user ALTER COLUMN ProfileVersion int NULL;
            ALTER TABLE dbo.fn_tenant_tenant ALTER COLUMN DefaultLocale varchar(35) NULL;
            UPDATE dbo.fn_identity_user SET PreferredLocale = NULL, ProfileVersion = NULL WHERE Id = @UserId;
            UPDATE dbo.fn_tenant_tenant SET DefaultLocale = NULL WHERE Id = @TenantId;
            """,
            new { UserId = userId, TenantId = tenantId });

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);
    }

    private DbUpMigrationRunner CreateRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = _container.GetConnectionString(),
        }),
        NullLoggerFactory.Instance);

    private static async Task AssertLocalizationStateAsync(
        SqlConnection connection,
        Guid userId,
        Guid tenantId)
    {
        var values = await connection.QuerySingleAsync<LocalizationValues>(
            """
            SELECT identityUser.PreferredLocale,
                   identityUser.ProfileVersion,
                   tenantObject.DefaultLocale
            FROM dbo.fn_identity_user AS identityUser
            CROSS JOIN dbo.fn_tenant_tenant AS tenantObject
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
                   CAST(CHARACTER_MAXIMUM_LENGTH AS bigint) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
        AssertContainsIgnoreCase(indexes, "PK_fn_identity_user_role");
        AssertContainsIgnoreCase(indexes, "PK_fn_identity_role_permission");
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
            StringAssert.Contains(column.ColumnDefault, "zh-CN");
        }

        var version = columns.Single(item => item.Name == "ProfileVersion");
        Assert.AreEqual("NO", version.IsNullable, ignoreCase: true);
        StringAssert.Contains(version.ColumnDefault, "1");
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
