using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class SqlServerMigrationTests
{
    private string _connectionString = null!;

    [TestInitialize]
    public async Task StartAsync() =>
        _connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();

    [TestMethod]
    public async Task SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema()
    {
        var runner = new DbUpMigrationRunner(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = _connectionString,
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

        await using var connection = new SqlConnection(_connectionString);
        var tables = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo'"))
            .ToArray();

        AssertContainsIgnoreCase(tables, "fn_tenancy_tenant");
        AssertContainsIgnoreCase(tables, "fn_tenancy_tenant_package");
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
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
            """);
        Assert.AreEqual(19, seedAuditColumnCount);
        var seedAuditContractCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
            FROM sys.foreign_keys
            WHERE name = 'FK_fn_seed_run_item_Run'
            """);
        Assert.AreEqual(1, seedAuditForeignKeyCount);

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
        var superAdministratorColumns = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
                   CAST(CHARACTER_MAXIMUM_LENGTH AS bigint) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
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
    public async Task SqlServer_outbox_dead_letter_migration_recovers_partial_state()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_outbox_message DROP COLUMN DeadLetterReasonCode;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%017_OutboxDeadLetter.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME IN ('DeadLetteredAtUtc', 'DeadLetterReasonCode')
            """));
    }

    [TestMethod]
    public async Task UuidBinaryExpand_SqlServer_pairs_008_without_binary_shadow_columns()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = new SqlConnection(_connectionString);

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql'"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND COLUMN_NAME LIKE '%Binary'
            """));
    }

    [TestMethod]
    public async Task SqlServer_seed_audit_migration_recovers_after_first_table_commit()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_seed_run_item;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%007_SeedExecutionAudit.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        var tableCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME IN ('fn_seed_run', 'fn_seed_run_item')
            """);
        Assert.AreEqual(2, tableCount);
    }

    [TestMethod]
    public async Task SqlServer_localization_migration_recovers_legacy_and_partial_states()
    {
        // 004 恢复必须停在 naming Contract 之前：011 会 DROP fn_tenant_tenant。
        await MigrateSqlServerThrough008Async();

        await using var connection = new SqlConnection(_connectionString);
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

        var legacyUpgrade = await MigrateSqlServerThrough008Async();
        Assert.AreEqual(1, legacyUpgrade.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);

        // 模拟列已存在但约束未收紧、DbUp 尚未记账的恢复路径。
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

        var recovered = await MigrateSqlServerThrough008Async();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertLocalizationStateAsync(connection, userId, tenantId);
    }

    [TestMethod]
    public async Task SqlServer_super_administrator_migration_recovers_partial_state()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(_connectionString);
        var roleId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_identity_role
                DROP CONSTRAINT CK_fn_identity_role_SuperAdministratorScope;
            ALTER TABLE dbo.fn_identity_role
                DROP CONSTRAINT DF_fn_identity_role_IsSuperAdministrator;
            ALTER TABLE dbo.fn_identity_role
                ALTER COLUMN IsSuperAdministrator bit NULL;
            INSERT INTO dbo.fn_identity_role
                (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
                 IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@RoleId, NULL, 'host', 'host-administrator', N'Host Administrator',
                 1, 1, NULL, @Now, NULL, 1);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%005_SuperAdministrator.sql';
            """,
            new { RoleId = roleId, Now = DateTimeOffset.UtcNow });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await connection.QuerySingleAsync<bool>(
            "SELECT IsSuperAdministrator FROM dbo.fn_identity_role WHERE Id = @RoleId",
            new { RoleId = roleId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'fn_identity_role'
              AND COLUMN_NAME = 'IsSuperAdministrator' AND IS_NULLABLE = 'NO'
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE name = 'CK_fn_identity_role_SuperAdministratorScope'
            """));
    }


    private Task<MigrationResult> MigrateSqlServerThrough008Async()
    {
        var result = DbUp.DeployChanges.To.SqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && !name.EndsWith("009_UuidBinaryContract.sql", StringComparison.Ordinal)
                    && !name.EndsWith("010_NamingExpand.sql", StringComparison.Ordinal)
                    && !name.EndsWith("011_NamingContract.sql", StringComparison.Ordinal))
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    private DbUpMigrationRunner CreateRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = _connectionString,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        ContractOptions(),
        MigrationContractOptionFactory.NamingOptions());

    private static IOptions<UuidBinaryContractOptions> ContractOptions() =>
        MigrationContractOptionFactory.UuidOptions();

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
        AssertContainsIgnoreCase(names, "OccurredAtUtc");
        AssertContainsIgnoreCase(names, "ProcessedAtUtc");
        AssertContainsIgnoreCase(names, "SchemaVersion");
        AssertContainsIgnoreCase(names, "ContentType");
        AssertContainsIgnoreCase(names, "TenantId");
        AssertContainsIgnoreCase(names, "TraceId");
        AssertContainsIgnoreCase(names, "Payload");
        AssertContainsIgnoreCase(names, "DeadLetteredAtUtc");
        AssertContainsIgnoreCase(names, "DeadLetterReasonCode");
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
        var preferred = columns.Single(item =>
            item.Name == "PreferredLocale"
            && string.Equals(item.TableName, "fn_identity_user", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("NO", preferred.IsNullable, ignoreCase: true);
        Assert.AreEqual(35L, preferred.MaximumLength);
        StringAssert.Contains(preferred.ColumnDefault, "zh-CN");

        var defaultLocale = columns.Single(item =>
            item.Name == "DefaultLocale"
            && (string.Equals(item.TableName, "fn_tenancy_tenant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TableName, "fn_tenant_tenant", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("NO", defaultLocale.IsNullable, ignoreCase: true);
        Assert.AreEqual(35L, defaultLocale.MaximumLength);
        StringAssert.Contains(defaultLocale.ColumnDefault, "zh-CN");

        var version = columns.Single(item =>
            item.Name == "ProfileVersion"
            && string.Equals(item.TableName, "fn_identity_user", StringComparison.OrdinalIgnoreCase));
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
