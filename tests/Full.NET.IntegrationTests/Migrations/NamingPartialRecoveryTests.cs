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
public sealed class NamingPartialRecoveryTests
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
    public async Task NamingPartialRecovery_MySql_recreates_missing_tenancy_table()
    {
        await PrepareMySqlExpandStateAsync();
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync("DROP TABLE fn_tenancy_tenant");
        await connection.ExecuteAsync(
            "DELETE FROM schemaversions WHERE ScriptName LIKE '%010_NamingExpand.sql'");

        var recovery = await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlContainer!.GetConnectionString());

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_tenancy_tenant"));
    }

    [TestMethod]
    public async Task NamingPartialRecovery_MySql_completes_partial_outbox_backfill()
    {
        await PrepareMySqlExpandStateAsync();
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message SET MessageType = NULL;
            ALTER TABLE fn_outbox_message
                DROP COLUMN MessageType,
                DROP COLUMN OccurredAtUtc,
                DROP COLUMN ProcessedAtUtc,
                DROP COLUMN NextAttemptAtUtc,
                DROP COLUMN LockedUntilUtc;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%010_NamingExpand.sql';
            """);

        var recovery = await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlContainer!.GetConnectionString());

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM fn_outbox_message
            WHERE MessageType = Type AND OccurredAtUtc = OccurredAt
            """));
    }

    [TestMethod]
    public async Task NamingPartialRecovery_SqlServer_recreates_missing_tenancy_table()
    {
        await using var container = await StartSqlServerContainerAsync();
        await PrepareSqlServerExpandStateAsync(container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync("DROP TABLE dbo.fn_tenancy_tenant");
        await connection.ExecuteAsync(
            "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%010_NamingExpand.sql'");

        var recovery = await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(
            container.GetConnectionString());

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_tenancy_tenant"));
    }

    [TestMethod]
    public async Task NamingPartialRecovery_SqlServer_completes_partial_outbox_backfill()
    {
        await using var container = await StartSqlServerContainerAsync();
        await PrepareSqlServerExpandStateAsync(container.GetConnectionString());
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            UPDATE dbo.fn_outbox_message SET MessageType = NULL;
            ALTER TABLE dbo.fn_outbox_message
                DROP COLUMN MessageType,
                    OccurredAtUtc,
                    ProcessedAtUtc,
                    NextAttemptAtUtc,
                    LockedUntilUtc;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%010_NamingExpand.sql';
            """);

        var recovery = await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(
            container.GetConnectionString());

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.fn_outbox_message
            WHERE MessageType = Type AND OccurredAtUtc = OccurredAt
            """));
    }

    private async Task PrepareMySqlExpandStateAsync()
    {
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(
            _mySqlContainer!.GetConnectionString());
        await using var connection = CreateMySqlConnection();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlContainer!.GetConnectionString());
    }

    private static async Task PrepareSqlServerExpandStateAsync(string connectionString)
    {
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(connectionString);
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
