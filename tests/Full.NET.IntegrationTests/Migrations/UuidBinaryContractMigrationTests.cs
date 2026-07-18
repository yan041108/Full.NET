using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MySql;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class UuidBinaryContractMigrationTests
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
    public async Task UuidBinaryContract_MySql_records_paired_contract_migration()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schemaversions WHERE ScriptName LIKE '%009_UuidBinaryContract.sql'"));
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_switches_all_uuid_columns_and_removes_expand_objects()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());

        Assert.AreEqual(23, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND COLUMN_NAME IN
              (
                  'Id', 'TenantId', 'LockId', 'UserId', 'FamilyId', 'ReplacedById',
                  'ActiveTenantId', 'SessionId', 'ContextTenantId', 'ActorUserId',
                  'RoleId', 'RunId'
              )
              AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16
            """));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND (COLUMN_NAME LIKE '%Binary' OR COLUMN_NAME LIKE '%Legacy')
            """));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE() AND TRIGGER_NAME LIKE 'TR_%_UuidBinary_B%'
            """));
        Assert.AreEqual("Binary16", await connection.ExecuteScalarAsync<string>(
            "SELECT SchemaMode FROM fn_uuid_contract_state WHERE Id = 1"));
        Assert.AreEqual(10, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME IN
                  ('fn_tenant_tenant', 'fn_outbox_message', 'fn_identity_user',
                   'fn_identity_refresh_session', 'fn_identity_auth_audit', 'fn_identity_role',
                   'fn_identity_user_role', 'fn_identity_role_permission',
                   'fn_seed_run', 'fn_seed_run_item')
              AND CONSTRAINT_TYPE = 'PRIMARY KEY'
            """));
        Assert.AreEqual(6, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_NAME IN
                  ('FK_fn_identity_refresh_session_User', 'FK_fn_identity_auth_audit_User',
                   'FK_fn_identity_user_role_User', 'FK_fn_identity_user_role_Role',
                   'FK_fn_identity_role_permission_Role', 'FK_fn_seed_run_item_Run')
            """));
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT CONCAT(TABLE_NAME, ':', INDEX_NAME))
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND INDEX_NAME IN
                  ('IX_fn_identity_refresh_session_Family',
                   'IX_fn_identity_refresh_session_User',
                   'IX_fn_identity_auth_audit_User', 'IX_fn_identity_role_Tenant')
            """));
    }

    [TestMethod]
    [DataRow(false, true, true, "test-approval", "maintenance mode")]
    [DataRow(true, false, true, "test-approval", "verified backup")]
    [DataRow(true, true, false, "test-approval", "legacy writers stopped")]
    [DataRow(true, true, true, "", "destructive DDL approval")]
    public async Task UuidBinaryContract_MySql_rejects_missing_maintenance_evidence(
        bool maintenanceMode,
        bool backupVerified,
        bool legacyWritersStopped,
        string approvalId,
        string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner(
                maintenanceMode,
                backupVerified,
                legacyWritersStopped,
                approvalId).MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message, expectedMessage);
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_rejects_incomplete_008_schema()
    {
        await ApplyExpandAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            "ALTER TABLE fn_tenant_tenant DROP COLUMN IdBinary");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner().MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message,
            "UUID shadow schema mismatch: fn_tenant_tenant.IdBinary");
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_rejects_shadow_null_or_conflict()
    {
        await ApplyExpandAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES
                ('01890f4e-7c2a-7abc-8def-0123456789ab', 'contract-null', 'Contract Null',
                 'contract-null.local', true, UTC_TIMESTAMP(6), NULL, 1);
            DROP TRIGGER TR_fn_tenant_tenant_UuidBinary_BU;
            ALTER TABLE fn_tenant_tenant MODIFY COLUMN IdBinary BINARY(16) NULL;
            UPDATE fn_tenant_tenant SET IdBinary = NULL WHERE Identifier = 'contract-null';
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER TR_fn_tenant_tenant_UuidBinary_BU
            BEFORE UPDATE ON fn_tenant_tenant FOR EACH ROW
            SET NEW.IdBinary = NEW.IdBinary
            ;
            CREATE TABLE fn_uuid_contract_state
            (
                Id tinyint NOT NULL PRIMARY KEY,
                SchemaMode varchar(16) NOT NULL,
                DestructiveDdlApprovalId varchar(64) NOT NULL,
                UpdatedAtUtc datetime(6) NOT NULL
            )
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner().MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message,
            "UUID contract data mismatch: fn_tenant_tenant.Id");
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_rejects_missing_sync_trigger()
    {
        await ApplyExpandAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync("DROP TRIGGER TR_fn_seed_run_UuidBinary_BU");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner().MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message,
            "UUID contract prerequisite missing: expand triggers");
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_rejects_reference_mismatch()
    {
        await ApplyExpandAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName, PasswordHash,
                 IsActive, FailedLoginCount, LockoutEndUtc, SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                ('01890f4e-7c2a-7abc-8def-0123456789ab', NULL, 'host', 'contract-reference',
                 'CONTRACT-REFERENCE', 'Contract Reference', 'unused', true, 0, NULL, 'stamp',
                 UTC_TIMESTAMP(6), NULL, 1);
            DROP TRIGGER TR_fn_identity_user_UuidBinary_BU;
            UPDATE fn_identity_user
            SET TenantId = '019822d3-0700-7000-8000-000000000299',
                TenantIdBinary = UUID_TO_BIN('019822d3-0700-7000-8000-000000000299', 0)
            WHERE Username = 'contract-reference';
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER TR_fn_identity_user_UuidBinary_BU
            BEFORE UPDATE ON fn_identity_user FOR EACH ROW
            SET NEW.IdBinary = UUID_TO_BIN(NEW.Id, 0)
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner().MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message,
            "UUID contract reference mismatch: fn_identity_user.TenantId");
    }

    [TestMethod]
    public async Task UuidBinaryContract_MySql_rejects_wrong_canonical_type()
    {
        await ApplyExpandAsync();
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.ExecuteAsync(
            "ALTER TABLE fn_tenant_tenant MODIFY COLUMN Id varchar(36) NOT NULL");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateRunner().MigrateAsync());

        StringAssert.Contains(exception.InnerException?.Message ?? exception.Message,
            "UUID canonical schema mismatch: fn_tenant_tenant.Id");
    }

    [TestMethod]
    public async Task UuidBinaryContract_SqlServer_governs_explicit_clustered_indexes()
    {
        await using var sqlContainer = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await sqlContainer.StartAsync();
        try
        {
            var runner = CreateSqlServerRunner(sqlContainer.GetConnectionString());
            await runner.MigrateAsync();
            await using var connection = new SqlConnection(sqlContainer.GetConnectionString());

            Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.key_constraints keyObject
                INNER JOIN sys.indexes indexObject
                    ON indexObject.object_id = keyObject.parent_object_id
                   AND indexObject.index_id = keyObject.unique_index_id
                WHERE OBJECT_NAME(keyObject.parent_object_id) IN
                      ('fn_outbox_message', 'fn_identity_auth_audit')
                  AND keyObject.type = 'PK' AND indexObject.type_desc = 'NONCLUSTERED'
                """));
            Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.indexes
                WHERE name IN
                      ('IX_fn_outbox_message_OccurredAt_Id',
                       'IX_fn_identity_auth_audit_OccurredAtUtc_Id')
                  AND type_desc = 'CLUSTERED'
                """));
        }
        finally
        {
            await sqlContainer.DisposeAsync();
        }
    }

    private DbUpMigrationRunner CreateRunner(
        bool maintenanceMode = true,
        bool backupVerified = true,
        bool legacyWritersStopped = true,
        string approvalId = "test-uuid-contract-009") => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = _container.GetConnectionString(),
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = maintenanceMode,
            BackupVerified = backupVerified,
            LegacyWritersStopped = legacyWritersStopped,
            DestructiveDdlApprovalId = approvalId,
        }));

    private async Task ApplyExpandAsync()
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    _container.GetConnectionString(),
                    MySqlGuidStorageMode.LegacyChar36,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && !name.EndsWith("009_UuidBinaryContract.sql", StringComparison.Ordinal))
            .Build()
            .PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        await Task.CompletedTask;
    }

    private static DbUpMigrationRunner CreateSqlServerRunner(string connectionString) => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = "test-uuid-contract-009",
        }));
}
