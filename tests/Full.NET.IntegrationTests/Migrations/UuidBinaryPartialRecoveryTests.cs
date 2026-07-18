using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class UuidBinaryPartialRecoveryTests
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
    public async Task UuidBinaryPartialRecovery_existing_identity_outbox_and_seed_graph_is_backfilled()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await DropExpandObjectsAsync(connection);
        await InsertLegacyGraphAsync(connection);

        await runner.MigrateAsync();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_tenant_tenant AS tenant
            CROSS JOIN fn_outbox_message AS outbox
            CROSS JOIN fn_identity_user AS userObject
            CROSS JOIN fn_identity_refresh_session AS sessionObject
            CROSS JOIN fn_identity_auth_audit AS audit
            CROSS JOIN fn_identity_role AS roleObject
            CROSS JOIN fn_identity_user_role AS userRole
            CROSS JOIN fn_identity_role_permission AS rolePermission
            CROSS JOIN fn_seed_run AS seedRun
            CROSS JOIN fn_seed_run_item AS seedItem
            WHERE tenant.IdBinary = UUID_TO_BIN(tenant.Id, 0)
              AND outbox.IdBinary = UUID_TO_BIN(outbox.Id, 0)
              AND outbox.TenantIdBinary = UUID_TO_BIN(outbox.TenantId, 0)
              AND outbox.LockIdBinary = UUID_TO_BIN(outbox.LockId, 0)
              AND userObject.IdBinary = UUID_TO_BIN(userObject.Id, 0)
              AND userObject.TenantIdBinary = UUID_TO_BIN(userObject.TenantId, 0)
              AND sessionObject.IdBinary = UUID_TO_BIN(sessionObject.Id, 0)
              AND sessionObject.UserIdBinary = UUID_TO_BIN(sessionObject.UserId, 0)
              AND sessionObject.FamilyIdBinary = UUID_TO_BIN(sessionObject.FamilyId, 0)
              AND sessionObject.ReplacedByIdBinary = UUID_TO_BIN(sessionObject.ReplacedById, 0)
              AND sessionObject.ActiveTenantIdBinary = UUID_TO_BIN(sessionObject.ActiveTenantId, 0)
              AND audit.IdBinary = UUID_TO_BIN(audit.Id, 0)
              AND audit.UserIdBinary = UUID_TO_BIN(audit.UserId, 0)
              AND audit.SessionIdBinary = UUID_TO_BIN(audit.SessionId, 0)
              AND audit.ContextTenantIdBinary = UUID_TO_BIN(audit.ContextTenantId, 0)
              AND audit.ActorUserIdBinary = UUID_TO_BIN(audit.ActorUserId, 0)
              AND roleObject.IdBinary = UUID_TO_BIN(roleObject.Id, 0)
              AND roleObject.TenantIdBinary = UUID_TO_BIN(roleObject.TenantId, 0)
              AND userRole.UserIdBinary = UUID_TO_BIN(userRole.UserId, 0)
              AND userRole.RoleIdBinary = UUID_TO_BIN(userRole.RoleId, 0)
              AND rolePermission.RoleIdBinary = UUID_TO_BIN(rolePermission.RoleId, 0)
              AND seedRun.IdBinary = UUID_TO_BIN(seedRun.Id, 0)
              AND seedItem.RunIdBinary = UUID_TO_BIN(seedItem.RunId, 0)
            """));
    }

    [TestMethod]
    public async Task UuidBinaryPartialRecovery_preflight_rejects_invalid_duplicate_and_missing_reference_data()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await DropExpandObjectsAsync(connection);

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES ('not-a-uuid', 'invalid-preflight', 'Invalid', 'invalid-preflight.local', true, UTC_TIMESTAMP(6), NULL, 1)
            """);
        await AssertMigrationFailureAsync(runner, "Invalid UUID: fn_tenant_tenant.Id count=1");
        await connection.ExecuteAsync("DELETE FROM fn_tenant_tenant WHERE Identifier = 'invalid-preflight'");

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_outbox_message
                MODIFY COLUMN Id char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL;
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES
                ('01890f4e-7c2a-7abc-8def-0123456789ab', 'duplicate-a', 1, 'application/json', NULL,
                 NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL),
                ('01890F4E-7C2A-7ABC-8DEF-0123456789AB', 'duplicate-b', 1, 'application/json', NULL,
                 NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL)
            """);
        await AssertMigrationFailureAsync(runner, "Duplicate UUID binary: fn_outbox_message.Id count=1");
        await connection.ExecuteAsync("DELETE FROM fn_outbox_message");

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES ('019822d3-0700-7000-8000-000000000201', 'missing-reference', 1, 'application/json',
                    '019822d3-0700-7000-8000-000000000299', NULL, X'7B7D', UTC_TIMESTAMP(6),
                    NULL, NULL, 0, NULL, NULL, NULL)
            """);
        await AssertMigrationFailureAsync(runner, "UUID reference missing: fn_outbox_message.TenantId count=1");
        await connection.ExecuteAsync("DELETE FROM fn_outbox_message");

        var recovery = await runner.MigrateAsync();
        Assert.AreEqual(1, recovery.ExecutedScriptCount);

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_outbox_message
                DROP INDEX UX_fn_outbox_message_IdBinary,
                ADD UNIQUE INDEX UX_fn_outbox_message_IdBinary (IdBinary(8));
            DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
            """);
        await AssertMigrationFailureAsync(runner, "UUID unique index mismatch: UX_fn_outbox_message_IdBinary");

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_outbox_message
                DROP INDEX UX_fn_outbox_message_IdBinary,
                ADD UNIQUE INDEX UX_fn_outbox_message_IdBinary (IdBinary, TenantIdBinary);
            """);
        await AssertMigrationFailureAsync(runner, "UUID unique index mismatch: UX_fn_outbox_message_IdBinary");

        await connection.ExecuteAsync(
            "ALTER TABLE fn_outbox_message DROP INDEX UX_fn_outbox_message_IdBinary");
        recovery = await runner.MigrateAsync();
        Assert.AreEqual(1, recovery.ExecutedScriptCount);

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES ('01890f4e-7c2a-7abc-8def-0123456789ab', 'unique-source', 1, 'application/json',
                    NULL, NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL)
            """);
        var insertCollision = await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES ('01890F4E-7C2A-7ABC-8DEF-0123456789AB', 'unique-insert-collision', 1, 'application/json',
                    NULL, NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL)
            """));
        Assert.AreEqual(1062, insertCollision.Number);

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES ('019822d3-0700-7000-8000-000000000210', 'unique-update-source', 1, 'application/json',
                    NULL, NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL)
            """);
        var updateCollision = await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET Id = '01890F4E-7C2A-7ABC-8DEF-0123456789AB'
            WHERE Id = '019822d3-0700-7000-8000-000000000210'
            """));
        Assert.AreEqual(1062, updateCollision.Number);
    }

    [TestMethod]
    public async Task UuidBinaryPartialRecovery_missing_shadow_column_is_recreated()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(
            """
            DROP TRIGGER TR_fn_outbox_message_UuidBinary_BI;
            DROP TRIGGER TR_fn_outbox_message_UuidBinary_BU;
            ALTER TABLE fn_outbox_message DROP COLUMN LockIdBinary;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
            """);

        var recovery = await runner.MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME = 'LockIdBinary' AND DATA_TYPE = 'binary'
            """));
    }

    [TestMethod]
    public async Task UuidBinaryPartialRecovery_partial_backfill_is_completed()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await InsertTenantAndOutboxAsync(connection);
        await connection.ExecuteAsync(
            """
            DROP TRIGGER TR_fn_outbox_message_UuidBinary_BU;
            UPDATE fn_outbox_message SET TenantIdBinary = NULL;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
            """);

        await runner.MigrateAsync();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_outbox_message WHERE TenantIdBinary = UUID_TO_BIN(TenantId, 0)"));
    }

    [TestMethod]
    public async Task UuidBinaryPartialRecovery_missing_triggers_are_recreated()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(
            """
            DROP TRIGGER TR_fn_seed_run_UuidBinary_BI;
            DROP TRIGGER TR_fn_seed_run_UuidBinary_BU;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
            """);

        await runner.MigrateAsync();

        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE()
              AND TRIGGER_NAME IN ('TR_fn_seed_run_UuidBinary_BI', 'TR_fn_seed_run_UuidBinary_BU')
            """));
    }

    [TestMethod]
    public async Task UuidBinaryPartialRecovery_unjournaled_complete_expand_converges()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_outbox_message MODIFY COLUMN TenantIdBinary BINARY(16) NOT NULL;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
            """);

        var recovery = await runner.MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(23, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME LIKE '%Binary'
              AND DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16
            """));
        Assert.AreEqual("YES", await connection.ExecuteScalarAsync<string>(
            """
            SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME = 'TenantIdBinary'
            """));
    }

    private static async Task InsertTenantAndOutboxAsync(MySqlConnection connection)
    {
        const string tenantId = "01890f4e-7c2a-7abc-8def-0123456789ab";
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES (@TenantId, 'recovery', 'Recovery', 'recovery.local', true, UTC_TIMESTAMP(6), NULL, 1);
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES ('019822d3-0700-7000-8000-000000000201', 'test', 1, 'application/json', @TenantId,
                    NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, NULL, NULL, NULL);
            """,
            new { TenantId = tenantId });
    }

    private static Task DropExpandObjectsAsync(MySqlConnection connection) => connection.ExecuteAsync(
        """
        DROP TRIGGER TR_fn_tenant_tenant_UuidBinary_BI;
        DROP TRIGGER TR_fn_tenant_tenant_UuidBinary_BU;
        DROP TRIGGER TR_fn_outbox_message_UuidBinary_BI;
        DROP TRIGGER TR_fn_outbox_message_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_user_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_user_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_refresh_session_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_refresh_session_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_auth_audit_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_auth_audit_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_role_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_role_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_user_role_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_user_role_UuidBinary_BU;
        DROP TRIGGER TR_fn_identity_role_permission_UuidBinary_BI;
        DROP TRIGGER TR_fn_identity_role_permission_UuidBinary_BU;
        DROP TRIGGER TR_fn_seed_run_UuidBinary_BI;
        DROP TRIGGER TR_fn_seed_run_UuidBinary_BU;
        DROP TRIGGER TR_fn_seed_run_item_UuidBinary_BI;
        DROP TRIGGER TR_fn_seed_run_item_UuidBinary_BU;
        ALTER TABLE fn_tenant_tenant DROP COLUMN IdBinary;
        ALTER TABLE fn_outbox_message DROP COLUMN IdBinary, DROP COLUMN TenantIdBinary, DROP COLUMN LockIdBinary;
        ALTER TABLE fn_identity_user DROP COLUMN IdBinary, DROP COLUMN TenantIdBinary;
        ALTER TABLE fn_identity_refresh_session DROP COLUMN IdBinary, DROP COLUMN UserIdBinary,
            DROP COLUMN FamilyIdBinary, DROP COLUMN ReplacedByIdBinary, DROP COLUMN ActiveTenantIdBinary;
        ALTER TABLE fn_identity_auth_audit DROP COLUMN IdBinary, DROP COLUMN UserIdBinary,
            DROP COLUMN SessionIdBinary, DROP COLUMN ContextTenantIdBinary, DROP COLUMN ActorUserIdBinary;
        ALTER TABLE fn_identity_role DROP COLUMN IdBinary, DROP COLUMN TenantIdBinary;
        ALTER TABLE fn_identity_user_role DROP COLUMN UserIdBinary, DROP COLUMN RoleIdBinary;
        ALTER TABLE fn_identity_role_permission DROP COLUMN RoleIdBinary;
        ALTER TABLE fn_seed_run DROP COLUMN IdBinary;
        ALTER TABLE fn_seed_run_item DROP COLUMN RunIdBinary;
        DELETE FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql';
        """);

    private static Task InsertLegacyGraphAsync(MySqlConnection connection) => connection.ExecuteAsync(
        """
        INSERT INTO fn_tenant_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
        VALUES ('01890f4e-7c2a-7abc-8def-0123456789ab', 'graph', 'Graph', 'graph.local', true, UTC_TIMESTAMP(6), NULL, 1);
        INSERT INTO fn_outbox_message
            (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
             ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
        VALUES ('019822d3-0700-7000-8000-000000000201', 'test', 1, 'application/json',
                '01890f4e-7c2a-7abc-8def-0123456789ab', NULL, X'7B7D', UTC_TIMESTAMP(6),
                NULL, NULL, 0, '019822d3-0700-7000-8000-000000000202', DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 1 MINUTE), NULL);
        INSERT INTO fn_identity_user
            (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName, PasswordHash,
             IsActive, FailedLoginCount, LockoutEndUtc, SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES ('019822d3-0700-7000-8000-000000000203', '01890f4e-7c2a-7abc-8def-0123456789ab',
                'tenant:graph', 'graph', 'GRAPH', 'Graph', 'unused', true, 0, NULL, 'stamp', UTC_TIMESTAMP(6), NULL, 1);
        INSERT INTO fn_identity_refresh_session
            (Id, UserId, FamilyId, ClientId, TokenHash, ExpiresAtUtc, ConsumedAtUtc, RevokedAtUtc,
             ReplacedById, CreatedAtUtc, Version, ActiveTenantId)
        VALUES ('019822d3-0700-7000-8000-000000000204', '019822d3-0700-7000-8000-000000000203',
                '019822d3-0700-7000-8000-000000000205', 'test', REPEAT('a', 64), DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 1 DAY),
                NULL, NULL, '019822d3-0700-7000-8000-000000000204', UTC_TIMESTAMP(6), 1,
                '01890f4e-7c2a-7abc-8def-0123456789ab');
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType, ResultCode, Succeeded,
             IpAddress, UserAgent, OccurredAtUtc, ContextTenantId, ActorUserId)
        VALUES ('019822d3-0700-7000-8000-000000000206', '019822d3-0700-7000-8000-000000000203',
                '019822d3-0700-7000-8000-000000000204', REPEAT('b', 64), 'login', 'ok', true,
                NULL, NULL, UTC_TIMESTAMP(6), '01890f4e-7c2a-7abc-8def-0123456789ab',
                '019822d3-0700-7000-8000-000000000203');
        INSERT INTO fn_identity_role
            (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive, IsSuperAdministrator,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES ('019822d3-0700-7000-8000-000000000207', '01890f4e-7c2a-7abc-8def-0123456789ab',
                'tenant:graph', 'graph-reader', 'Graph Reader', false, true, false, UTC_TIMESTAMP(6), NULL, 1);
        INSERT INTO fn_identity_user_role (UserId, RoleId)
        VALUES ('019822d3-0700-7000-8000-000000000203', '019822d3-0700-7000-8000-000000000207');
        INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
        VALUES ('019822d3-0700-7000-8000-000000000207', 'graph.read');
        INSERT INTO fn_seed_run
            (Id, Profile, EnvironmentName, Status, ApplicationVersion, CorrelationId, StartedAt, CompletedAt, ErrorCode)
        VALUES ('019822d3-0700-7000-8000-000000000208', 'Baseline', 'Test', 'Running', 'test', 'graph', UTC_TIMESTAMP(6), NULL, NULL);
        INSERT INTO fn_seed_run_item
            (RunId, Contributor, ContributorVersion, Status, CreatedCount, UpdatedCount, SkippedCount,
             StartedAt, CompletedAt, ErrorCode)
        VALUES ('019822d3-0700-7000-8000-000000000208', 'Graph', 1, 'Running', 0, 0, 0, UTC_TIMESTAMP(6), NULL, NULL);
        """);

    private static async Task AssertMigrationFailureAsync(
        DbUpMigrationRunner runner,
        string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync());
        StringAssert.Contains(exception.InnerException?.Message ?? string.Empty, expectedMessage);
    }

    private DbUpMigrationRunner CreateRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = _container.GetConnectionString(),
        }),
        NullLoggerFactory.Instance);

    private MySqlConnection CreateConnection() => new(_container.GetConnectionString());
}
