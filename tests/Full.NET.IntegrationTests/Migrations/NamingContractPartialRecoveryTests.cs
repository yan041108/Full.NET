using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class NamingContractPartialRecoveryTests
{
    private string _mySqlConnectionString = null!;

    [TestInitialize]
    public async Task StartMySqlAsync() =>
        _mySqlConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

    [TestMethod]
    public async Task NamingContractPartialRecovery_MySql_completes_after_legacy_columns_removed()
    {
        await NamingContractTestMigrationRunner.PrepareMySqlExpandStateAsync(
            _mySqlConnectionString);
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
            .CreateMySqlRunner(_mySqlConnectionString)
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
            _mySqlConnectionString);
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            DROP TABLE fn_tenant_tenant;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(_mySqlConnectionString)
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_tenancy_tenant"));
    }

    [TestMethod]
    public async Task NamingContractPartialRecovery_SqlServer_completes_after_legacy_columns_removed()
    {
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            sqlConnectionString);
        await using var connection = new SqlConnection(sqlConnectionString);
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
            .CreateSqlServerRunner(sqlConnectionString)
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(IIF(OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL, 0, 1) AS bit)"));
    }

    [TestMethod]
    public async Task NamingContractPartialRecovery_SqlServer_completes_after_legacy_tenant_dropped()
    {
        var sqlConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingContractTestMigrationRunner.PrepareSqlServerExpandStateAsync(
            sqlConnectionString);
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_tenant_tenant;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%011_NamingContract.sql';
            """);

        var recovery = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(sqlConnectionString)
            .MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_tenancy_tenant"));
    }

    private MySqlConnection CreateMySqlConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _mySqlConnectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
}
