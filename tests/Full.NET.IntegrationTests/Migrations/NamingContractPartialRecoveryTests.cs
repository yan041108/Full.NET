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
public sealed class NamingContractPartialRecoveryTests
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
    public async Task NamingContractPartialRecovery_MySql_completes_after_legacy_columns_removed()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_outbox_message DROP INDEX IX_fn_outbox_message_Pending;
            ALTER TABLE fn_outbox_message
                DROP COLUMN Type,
                DROP COLUMN OccurredAt,
                DROP COLUMN ProcessedAt,
                DROP COLUMN NextAttemptAt,
                DROP COLUMN LockedUntil;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlContainer!.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_tenant_tenant'
            """));
    }

    [TestMethod]
    public async Task NamingContractPartialRecovery_MySql_completes_after_legacy_tenant_dropped()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            DROP TABLE fn_tenant_tenant;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlContainer!.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_tenancy_tenant"));
    }

    [TestMethod]
    public async Task NamingContractPartialRecovery_SqlServer_completes_after_legacy_columns_removed()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_outbox_message_Pending ON dbo.fn_outbox_message;
            DROP INDEX IX_fn_outbox_message_OccurredAt_Id ON dbo.fn_outbox_message;
            ALTER TABLE dbo.fn_outbox_message
                DROP COLUMN Type,
                    OccurredAt,
                    ProcessedAt,
                    NextAttemptAt,
                    LockedUntil;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(container.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(IIF(OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL, 0, 1) AS bit)"));
    }

    [TestMethod]
    public async Task NamingContractPartialRecovery_SqlServer_completes_after_legacy_tenant_dropped()
    {
        await using var container = await StartSqlServerContainerAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_tenant_tenant;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(container.GetConnectionString())
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_tenancy_tenant"));
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
