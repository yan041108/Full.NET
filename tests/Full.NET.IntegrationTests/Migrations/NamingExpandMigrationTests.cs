using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
[DoNotParallelize]
public sealed class NamingExpandMigrationTests
{
    private MySqlContainer? _mySqlContainer;

    [TestInitialize]
    public async Task StartMySqlAsync()
    {
        _mySqlContainer = new MySqlBuilder("mysql:8.0")
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await _mySqlContainer.StartAsync();
    }

    [TestCleanup]
    public async Task CleanupMySqlAsync()
    {
        if (_mySqlContainer is not null)
        {
            await _mySqlContainer.DisposeAsync();
            _mySqlContainer = null;
        }
    }

    [TestMethod]
    public async Task NamingExpand_MySql_copies_tenant_and_outbox_mirror_columns()
    {
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);

        var expand = await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlContainer!.GetConnectionString());

        Assert.AreEqual(1, expand.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_tenant_tenant AS legacy
            INNER JOIN fn_tenancy_tenant AS canonical ON canonical.Id = legacy.Id
            WHERE canonical.Identifier = legacy.Identifier
              AND canonical.CreatedAtUtc = legacy.CreatedAt
              AND (canonical.UpdatedAtUtc <=> legacy.UpdatedAt)
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE MessageType = Type
              AND OccurredAtUtc = OccurredAt
              AND (ProcessedAtUtc <=> ProcessedAt)
              AND (NextAttemptAtUtc <=> NextAttemptAt)
              AND (LockedUntilUtc <=> LockedUntil)
            """));
    }

    [TestMethod]
    public async Task NamingExpand_MySql_records_paired_expand_migration()
    {
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schemaversions WHERE ScriptName LIKE '%010_NamingExpand.sql'"));
    }

    [TestMethod]
    public async Task NamingExpand_SqlServer_copies_tenant_and_outbox_mirror_columns()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(
            container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);

        var expand = await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(
            container.GetConnectionString());

        Assert.AreEqual(1, expand.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_tenant_tenant AS legacy
            INNER JOIN dbo.fn_tenancy_tenant AS canonical ON canonical.Id = legacy.Id
            WHERE canonical.Identifier = legacy.Identifier
              AND canonical.CreatedAtUtc = legacy.CreatedAt
              AND (canonical.UpdatedAtUtc = legacy.UpdatedAt OR (canonical.UpdatedAtUtc IS NULL AND legacy.UpdatedAt IS NULL))
            """));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_outbox_message
            WHERE MessageType = Type
              AND OccurredAtUtc = OccurredAt
              AND (ProcessedAtUtc = ProcessedAt OR (ProcessedAtUtc IS NULL AND ProcessedAt IS NULL))
              AND (NextAttemptAtUtc = NextAttemptAt OR (NextAttemptAtUtc IS NULL AND NextAttemptAt IS NULL))
              AND (LockedUntilUtc = LockedUntil OR (LockedUntilUtc IS NULL AND LockedUntil IS NULL))
            """));
    }

    [TestMethod]
    public async Task NamingExpand_SqlServer_records_paired_expand_migration()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(
            container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE '%010_NamingExpand.sql'"));
    }

    private MySqlConnection CreateMySqlConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _mySqlContainer!.GetConnectionString(),
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));

    private static async Task<MsSqlContainer> StartSqlServerContainerAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        return container;
    }
}

internal static class NamingExpandTestData
{
    public static Task InsertTenantAndOutboxAsync(MySqlConnection connection) =>
        connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES (UUID_TO_BIN(@TenantId), 'naming-expand', 'Naming Expand', 'naming-expand.local', true, UTC_TIMESTAMP(6), NULL, 1);
            INSERT INTO fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES (UUID_TO_BIN(@OutboxId), 'fullnet.tenancy.tenant-provisioned', 1, 'application/json', UUID_TO_BIN(@TenantId),
                    NULL, X'7B7D', UTC_TIMESTAMP(6), NULL, NULL, 0, UUID_TO_BIN(@LockId),
                    DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 5 MINUTE), NULL);
            """,
            new
            {
                TenantId = "01890f4e-7c2a-7abc-8def-0123456789ab",
                OutboxId = "019822d3-0700-7000-8000-000000000201",
                LockId = "019822d3-0700-7000-8000-000000000202",
            });

    public static Task InsertTenantAndOutboxAsync(SqlConnection connection) =>
        connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES (@TenantId, 'naming-expand', 'Naming Expand', 'naming-expand.local', 1, SYSUTCDATETIME(), NULL, 1);
            INSERT INTO dbo.fn_outbox_message
                (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt,
                 ProcessedAt, NextAttemptAt, Attempts, LockId, LockedUntil, Error)
            VALUES (@OutboxId, 'fullnet.tenancy.tenant-provisioned', 1, 'application/json', @TenantId,
                    NULL, 0x7B7D, SYSUTCDATETIME(), NULL, NULL, 0, @LockId,
                    DATEADD(MINUTE, 5, SYSUTCDATETIME()), NULL);
            """,
            new
            {
                TenantId = Guid.Parse("01890f4e-7c2a-7abc-8def-0123456789ab"),
                OutboxId = Guid.Parse("019822d3-0700-7000-8000-000000000201"),
                LockId = Guid.Parse("019822d3-0700-7000-8000-000000000202"),
            });
}
