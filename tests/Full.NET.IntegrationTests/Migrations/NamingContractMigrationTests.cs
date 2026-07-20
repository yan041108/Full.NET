using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class NamingContractMigrationTests
{
    private string _mySqlConnectionString = null!;

    [TestInitialize]
    public async Task StartMySqlAsync() =>
        _mySqlConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

    [TestMethod]
    public async Task NamingContract_MySql_drops_legacy_objects_and_tightens_outbox()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlConnectionString);
        var contract = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlConnectionString)
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
            _mySqlConnectionString);
        await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlConnectionString)
            .MigrateAsync();
        await using var connection = CreateMySqlConnection();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schemaversions WHERE ScriptName LIKE '%011_NamingContract.sql'"));
    }

    [TestMethod]
    public async Task NamingContract_MySql_rejects_missing_maintenance_evidence()
    {
        // 同一 Expand 库上连续试 5 个门禁，避免 DataRow 各起一轮 Through010 准备。
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlConnectionString);

        (bool MaintenanceMode, bool BackupVerified, bool LegacyWritersStopped,
            bool LegacyOutboxDrained, string ApprovalId, string ExpectedMessage)[] cases =
        [
            (false, true, true, true, MigrationContractOptionFactory.NamingApprovalId, "maintenance mode"),
            (true, false, true, true, MigrationContractOptionFactory.NamingApprovalId, "verified backup"),
            (true, true, false, true, MigrationContractOptionFactory.NamingApprovalId, "legacy writers stopped"),
            (true, true, true, false, MigrationContractOptionFactory.NamingApprovalId, "legacy outbox drained"),
            (true, true, true, true, "", "destructive DDL approval"),
        ];

        foreach (var gate in cases)
        {
            var runner = new DbUpMigrationRunner(
                Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.MySql,
                    ConnectionString = _mySqlConnectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 300,
                }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(),
                Microsoft.Extensions.Options.Options.Create(new PreV1NamingContractOptions
                {
                    MaintenanceMode = gate.MaintenanceMode,
                    BackupVerified = gate.BackupVerified,
                    LegacyWritersStopped = gate.LegacyWritersStopped,
                    LegacyOutboxDrained = gate.LegacyOutboxDrained,
                    DestructiveDdlApprovalId = gate.ApprovalId,
                }));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.MigrateAsync());
            StringAssert.Contains(
                exception.InnerException?.Message ?? exception.Message,
                gate.ExpectedMessage);
        }
    }

    [TestMethod]
    public async Task NamingContract_MySql_rejects_legacy_pending_outbox()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlConnectionString);
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
                .CreateMySqlRunner(_mySqlConnectionString)
                .MigrateAsync());

        StringAssert.Contains(
            exception.InnerException?.Message ?? exception.Message,
            "legacy pending outbox");
    }

    [TestMethod]
    public async Task NamingContract_SqlServer_drops_legacy_objects_and_tightens_outbox()
    {
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            sqlConnectionString);
        var contract = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(sqlConnectionString)
            .MigrateAsync();

        Assert.AreEqual(1, contract.ExecutedScriptCount);
        await using var connection = new SqlConnection(sqlConnectionString);
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
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            sqlConnectionString);
        await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(sqlConnectionString)
            .MigrateAsync();
        await using var connection = new SqlConnection(sqlConnectionString);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE '%011_NamingContract.sql'"));
    }

    [TestMethod]
    public async Task NamingContract_SqlServer_rejects_tenant_count_mismatch()
    {
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            sqlConnectionString);
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.ExecuteAsync("DELETE FROM dbo.fn_tenancy_tenant");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NamingContractTestMigrationRunner
                .CreateSqlServerRunner(sqlConnectionString)
                .MigrateAsync());

        StringAssert.Contains(
            exception.InnerException?.Message ?? exception.Message,
            "tenant count mismatch");
    }

    private MySqlConnection CreateMySqlConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _mySqlConnectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
}
