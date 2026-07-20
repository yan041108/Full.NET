using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class NamingPartialRecoveryTests
{
    private string _mySqlConnectionString = null!;

    [TestInitialize]
    public async Task StartMySqlAsync() =>
        _mySqlConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

    [TestMethod]
    public async Task NamingPartialRecovery_MySql_recreates_missing_tenancy_table()
    {
        await PrepareMySqlExpandStateAsync();
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync("DROP TABLE fn_tenancy_tenant");
        await connection.ExecuteAsync(
            "DELETE FROM schemaversions WHERE ScriptName LIKE '%010_NamingExpand.sql'");

        var recovery = await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlConnectionString);

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
            _mySqlConnectionString);

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
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await PrepareSqlServerExpandStateAsync(sqlConnectionString);
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.ExecuteAsync("DROP TABLE dbo.fn_tenancy_tenant");
        await connection.ExecuteAsync(
            "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%010_NamingExpand.sql'");

        var recovery = await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(
            sqlConnectionString);

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_tenancy_tenant"));
    }

    [TestMethod]
    public async Task NamingPartialRecovery_SqlServer_completes_partial_outbox_backfill()
    {
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await PrepareSqlServerExpandStateAsync(sqlConnectionString);
        await using var connection = new SqlConnection(sqlConnectionString);
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
            sqlConnectionString);

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
            _mySqlConnectionString);
        await using var connection = CreateMySqlConnection();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(
            _mySqlConnectionString);
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
            _mySqlConnectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
}
