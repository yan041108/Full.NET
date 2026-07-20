using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
[DoNotParallelize]
public sealed class NamingContractMigrationTests
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
    public async Task NamingContract_MySql_drops_legacy_objects_and_tightens_outbox()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        var contract = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlContainer!.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, contract.ExecutedScriptCount);
        await using var connection = CreateMySqlConnection();
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_tenant_tenant'
            """));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME IN ('Type', 'OccurredAt', 'ProcessedAt', 'NextAttemptAt', 'LockedUntil')
            """));
        Assert.AreEqual("NO", await connection.ExecuteScalarAsync<string>(
            """
            SELECT IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME = 'MessageType'
            """));
        Assert.AreEqual("Contracted", await connection.ExecuteScalarAsync<string>(
            "SELECT SchemaMode FROM fn_pre_v1_naming_contract_state WHERE Id = 1"));
    }

    [TestMethod]
    public async Task NamingContract_MySql_records_paired_contract_migration()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlContainer!.GetConnectionString())
            .MigrateAsync();
        await using var connection = CreateMySqlConnection();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schemaversions WHERE ScriptName LIKE '%011_NamingContract.sql'"));
    }

    [TestMethod]
    [DataRow(false, true, true, true, MigrationContractOptionFactory.NamingApprovalId, "maintenance mode")]
    [DataRow(true, false, true, true, MigrationContractOptionFactory.NamingApprovalId, "verified backup")]
    [DataRow(true, true, false, true, MigrationContractOptionFactory.NamingApprovalId, "legacy writers stopped")]
    [DataRow(true, true, true, false, MigrationContractOptionFactory.NamingApprovalId, "legacy outbox drained")]
    [DataRow(true, true, true, true, "", "destructive DDL approval")]
    public async Task NamingContract_MySql_rejects_missing_maintenance_evidence(
        bool maintenanceMode,
        bool backupVerified,
        bool legacyWritersStopped,
        bool legacyOutboxDrained,
        string approvalId,
        string expectedMessage)
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        var runner = new DbUpMigrationRunner(
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = _mySqlContainer!.GetConnectionString(),
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            Microsoft.Extensions.Options.Options.Create(new PreV1NamingContractOptions
            {
                MaintenanceMode = maintenanceMode,
                BackupVerified = backupVerified,
                LegacyWritersStopped = legacyWritersStopped,
                LegacyOutboxDrained = legacyOutboxDrained,
                DestructiveDdlApprovalId = approvalId,
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync());
        StringAssert.Contains(
            exception.InnerException?.Message ?? exception.Message,
            expectedMessage);
    }

    [TestMethod]
    public async Task NamingContract_MySql_rejects_legacy_pending_outbox()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE fn_outbox_message SET MessageType = NULL WHERE ProcessedAtUtc IS NULL";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamingContractTestMigrationRunner
                .CreateMySqlRunner(_mySqlContainer!.GetConnectionString())
                .MigrateAsync());

        StringAssert.Contains(
            exception.InnerException?.Message ?? exception.Message,
            "legacy pending outbox");
    }

    [TestMethod]
    public async Task NamingContract_SqlServer_drops_legacy_objects_and_tightens_outbox()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            container.GetConnectionString());
        var contract = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(container.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, contract.ExecutedScriptCount);
        await using var connection = new SqlConnection(container.GetConnectionString());
        Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(IIF(OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL, 0, 1) AS bit)"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
              AND name IN (N'Type', N'OccurredAt', N'ProcessedAt', N'NextAttemptAt', N'LockedUntil')
            """));
        Assert.AreEqual("Contracted", await connection.ExecuteScalarAsync<string>(
            "SELECT SchemaMode FROM dbo.fn_pre_v1_naming_contract_state WHERE Id = 1"));
    }

    [TestMethod]
    public async Task NamingContract_SqlServer_records_paired_contract_migration()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            container.GetConnectionString());
        await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(container.GetConnectionString())
            .MigrateAsync();
        await using var connection = new SqlConnection(container.GetConnectionString());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE '%011_NamingContract.sql'"));
    }

    [TestMethod]
    public async Task NamingContract_SqlServer_rejects_tenant_count_mismatch()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync("DELETE FROM dbo.fn_tenancy_tenant");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamingContractTestMigrationRunner
                .CreateSqlServerRunner(container.GetConnectionString())
                .MigrateAsync());

        StringAssert.Contains(
            exception.InnerException?.Message ?? exception.Message,
            "tenant count mismatch");
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
