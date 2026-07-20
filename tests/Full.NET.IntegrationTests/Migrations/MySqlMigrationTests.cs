using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
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
        .WithCommand("--log-bin-trust-function-creators=1")
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
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());

        var first = await runner.MigrateAsync();
        var second = await runner.MigrateAsync();

        Assert.IsTrue(first.Successful);
        Assert.IsTrue(first.ExecutedScriptCount > 0);
        Assert.IsTrue(second.Successful);
        Assert.AreEqual(0, second.ExecutedScriptCount);

        await using var connection = CreateConnection();
        var tables = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE()"))
            .ToArray();

        AssertContainsIgnoreCase(tables, "fn_tenancy_tenant");
        Assert.IsFalse(tables.Contains("fn_tenant_tenant", StringComparer.OrdinalIgnoreCase));
        AssertContainsIgnoreCase(tables, "fn_outbox_message");
        AssertContainsIgnoreCase(tables, "fn_identity_user");
        AssertContainsIgnoreCase(tables, "fn_identity_refresh_session");
        AssertContainsIgnoreCase(tables, "fn_identity_auth_audit");
        AssertContainsIgnoreCase(tables, "fn_identity_role");
        AssertContainsIgnoreCase(tables, "fn_identity_user_role");
        AssertContainsIgnoreCase(tables, "fn_identity_role_permission");
        AssertContainsIgnoreCase(tables, "fn_seed_run");
        AssertContainsIgnoreCase(tables, "fn_seed_run_item");
        AssertContainsIgnoreCase(tables, "SchemaVersions");

        var seedAuditColumnCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
            """);
        Assert.AreEqual(19, seedAuditColumnCount);
        var seedAuditBinaryColumnCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND
              (
                  (TABLE_NAME = 'fn_seed_run' AND COLUMN_NAME = 'Id')
                  OR (TABLE_NAME = 'fn_seed_run_item' AND COLUMN_NAME = 'RunId')
              )
              AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16
            """);
        Assert.AreEqual(2, seedAuditBinaryColumnCount);
        var uuidBinaryShadowIndexCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT CONCAT(TABLE_NAME, ':', INDEX_NAME))
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND INDEX_NAME LIKE 'UX\_%\_IdBinary'
            """);
        Assert.AreEqual(0, uuidBinaryShadowIndexCount);
        var seedAuditContractCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND
              (
                  (TABLE_NAME = 'fn_seed_run_item'
                   AND COLUMN_NAME = 'Contributor'
                   AND CHARACTER_MAXIMUM_LENGTH = 128
                   AND IS_NULLABLE = 'NO')
                  OR (TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
                      AND COLUMN_NAME = 'ErrorCode'
                      AND CHARACTER_MAXIMUM_LENGTH = 128
                      AND IS_NULLABLE = 'YES')
                  OR (TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
                      AND COLUMN_NAME = 'CompletedAt'
                      AND IS_NULLABLE = 'YES')
              )
            """);
        Assert.AreEqual(5, seedAuditContractCount);
        var seedAuditForeignKeyCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_NAME = 'FK_fn_seed_run_item_Run'
            """);
        Assert.AreEqual(1, seedAuditForeignKeyCount);

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
        var superAdministratorColumns = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_identity_role'
              AND COLUMN_NAME = 'IsSuperAdministrator'
              AND IS_NULLABLE = 'NO'
            """);
        Assert.AreEqual(1, superAdministratorColumns);

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
                  OR (TABLE_NAME = 'fn_tenancy_tenant'
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
    public async Task MySql_seed_audit_migration_recovers_after_first_table_commit()
    {
        var runner = CreateExpandRunner();
        await runner.MigrateAsync();

        await using var connection = CreateLegacyConnection();
        await connection.ExecuteAsync(
            """
            DROP TABLE fn_seed_run_item;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%007_SeedExecutionAudit.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        var tableCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
            """);
        Assert.AreEqual(2, tableCount);
    }

    [TestMethod]
    public async Task MySql_localization_migration_recovers_legacy_and_partial_states()
    {
        var runner = CreateExpandRunner();
        await runner.MigrateAsync();

        await using var connection = CreateLegacyConnection();
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

    [TestMethod]
    public async Task MySql_super_administrator_migration_recovers_partial_state()
    {
        var runner = CreateExpandRunner();
        await runner.MigrateAsync();

        await using var connection = CreateLegacyConnection();
        var roleId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_identity_role
                DROP CONSTRAINT CK_fn_identity_role_SuperAdministratorScope;
            ALTER TABLE fn_identity_role
                MODIFY COLUMN IsSuperAdministrator boolean NULL DEFAULT NULL;
            INSERT INTO fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', 'host-administrator', '超级管理员',
                 true, true, NULL, @Now, NULL, 1);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%005_SuperAdministrator.sql';
            """,
            new { RoleId = roleId, Now = DateTime.UtcNow });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await connection.QuerySingleAsync<bool>(
            "SELECT IsSuperAdministrator FROM fn_identity_role WHERE Id = @RoleId",
            new { RoleId = roleId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_role'
              AND COLUMN_NAME = 'IsSuperAdministrator' AND IS_NULLABLE = 'NO'
              AND COLUMN_DEFAULT = '0'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_role'
              AND CONSTRAINT_NAME = 'CK_fn_identity_role_SuperAdministratorScope'
            """));
    }

    private DbUpMigrationRunner CreateRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = _container.GetConnectionString(),
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        MigrationContractOptionFactory.UuidOptions(),
        MigrationContractOptionFactory.NamingOptions());

    private MySqlConnection CreateConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _container.GetConnectionString(),
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));

    private IDatabaseMigrationRunner CreateExpandRunner() =>
        new UuidBinaryExpandTestMigrationRunner(_container.GetConnectionString());

    private MySqlConnection CreateLegacyConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _container.GetConnectionString(),
            MySqlGuidStorageMode.LegacyChar36,
            allowUserVariables: false));

    private static IOptions<UuidBinaryContractOptions> ContractOptions() =>
        MigrationContractOptionFactory.UuidOptions();

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
              AND TABLE_NAME IN ('fn_identity_user', 'fn_tenant_tenant')
              AND COLUMN_NAME IN ('PreferredLocale', 'ProfileVersion', 'DefaultLocale')
            """))
            .ToArray();
        AssertLocalizationColumns(columns);
    }

    private static void AssertRequiredOutboxColumns(IEnumerable<ColumnMetadata> columns)
    {
        var names = columns.Select(column => column.Name).ToArray();
        AssertContainsIgnoreCase(names, "MessageType");
        AssertContainsIgnoreCase(names, "SchemaVersion");
        AssertContainsIgnoreCase(names, "OccurredAtUtc");
        AssertContainsIgnoreCase(names, "ProcessedAtUtc");
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
        var preferred = columns.Single(item =>
            item.Name == "PreferredLocale"
            && string.Equals(item.TableName, "fn_identity_user", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("NO", preferred.IsNullable, ignoreCase: true);
        Assert.AreEqual(35L, preferred.MaximumLength);
        Assert.AreEqual("zh-CN", preferred.ColumnDefault);

        var defaultLocale = columns.Single(item =>
            item.Name == "DefaultLocale"
            && string.Equals(item.TableName, "fn_tenancy_tenant", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("NO", defaultLocale.IsNullable, ignoreCase: true);
        Assert.AreEqual(35L, defaultLocale.MaximumLength);
        Assert.AreEqual("zh-CN", defaultLocale.ColumnDefault);

        var version = columns.Single(item =>
            item.Name == "ProfileVersion"
            && string.Equals(item.TableName, "fn_identity_user", StringComparison.OrdinalIgnoreCase));
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
