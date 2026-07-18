using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class UuidBinaryExpandMigrationTests
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
    public async Task UuidBinaryExpand_MySql_creates_all_registered_columns_and_triggers()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = CreateConnection();

        Assert.AreEqual(23, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME LIKE '%Binary'
              AND DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16
            """));
        Assert.AreEqual(20, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE() AND TRIGGER_NAME LIKE 'TR_%_UuidBinary_B%'
            """));
    }

    [TestMethod]
    public async Task UuidBinaryExpand_MySql_records_paired_expand_migration()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = CreateConnection();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schemaversions WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql'"));
    }

    [TestMethod]
    public async Task UuidBinaryExpand_legacy_insert_and_update_keep_rfc_binary_in_sync()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = CreateConnection();
        const string firstId = "01890f4e-7c2a-7abc-8def-0123456789ab";
        const string secondId = "019822d3-0700-7000-8000-000000000201";

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES (@Id, 'uuid-expand', 'UUID Expand', 'uuid-expand.local', true, UTC_TIMESTAMP(6), NULL, 1)
            """,
            new { Id = firstId });
        Assert.AreEqual("01890F4E7C2A7ABC8DEF0123456789AB", await connection.ExecuteScalarAsync<string>(
            "SELECT HEX(IdBinary) FROM fn_tenant_tenant WHERE Identifier = 'uuid-expand'"));

        await connection.ExecuteAsync(
            "UPDATE fn_tenant_tenant SET Id = @Id WHERE Identifier = 'uuid-expand'",
            new { Id = secondId });
        Assert.AreEqual("019822D3070070008000000000000201", await connection.ExecuteScalarAsync<string>(
            "SELECT HEX(IdBinary) FROM fn_tenant_tenant WHERE Identifier = 'uuid-expand'"));
    }

    [TestMethod]
    public async Task UuidBinaryExpand_explicit_shadow_conflict_is_rejected_without_value_disclosure()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = CreateConnection();

        var exception = await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, IdBinary, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES ('01890f4e-7c2a-7abc-8def-0123456789ab',
                    UUID_TO_BIN('019822d3-0700-7000-8000-000000000201', 0),
                    'uuid-conflict', 'UUID Conflict', 'uuid-conflict.local', true, UTC_TIMESTAMP(6), NULL, 1)
            """));

        StringAssert.Contains(exception.Message, "UUID shadow conflict: fn_tenant_tenant.Id");
        Assert.IsFalse(exception.Message.Contains("01890f4e", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UuidBinaryExpand_invalid_legacy_uuid_is_rejected_without_value_disclosure()
    {
        await CreateRunner().MigrateAsync();
        await using var connection = CreateConnection();

        var exception = await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES ('not-a-uuid', 'uuid-invalid', 'UUID Invalid', 'uuid-invalid.local', true, UTC_TIMESTAMP(6), NULL, 1)
            """));

        StringAssert.Contains(exception.Message, "Invalid UUID: fn_tenant_tenant.Id");
        Assert.IsFalse(exception.Message.Contains("not-a-uuid", StringComparison.Ordinal));

        var compactException = await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_tenant_tenant
                (Id, Identifier, Name, Domain, IsActive, CreatedAt, UpdatedAt, Version)
            VALUES ('01890f4e7c2a7abc8def0123456789ab', 'uuid-compact', 'UUID Compact',
                    'uuid-compact.local', true, UTC_TIMESTAMP(6), NULL, 1)
            """));
        StringAssert.Contains(compactException.Message, "Invalid UUID: fn_tenant_tenant.Id");
    }

    [TestMethod]
    public async Task UuidBinaryExpand_DbUp_recorded_rerun_executes_no_scripts()
    {
        var runner = CreateRunner();
        await runner.MigrateAsync();

        var rerun = await runner.MigrateAsync();

        Assert.AreEqual(0, rerun.ExecutedScriptCount);
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
