using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 142 公告已读回执表可幂等恢复。</summary>
[TestClass]
public sealed class Migration142NotificationsAnnouncementReadReceiptRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_recreates_read_receipt_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await DropReceiptTableAsync(connection, isSqlServer: true);
        await DeleteMigrationRecordAsync(connection, isSqlServer: true);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await TableExistsAsync(connection, isSqlServer: true));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_recreates_read_receipt_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await DropReceiptTableAsync(connection, isSqlServer: false);
        await DeleteMigrationRecordAsync(connection, isSqlServer: false);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(await TableExistsAsync(connection, isSqlServer: false));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task DropReceiptTableAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer)
    {
        var sql = isSqlServer
            ? "DROP TABLE IF EXISTS dbo.fn_notifications_announcement_read_receipt;"
            : "DROP TABLE IF EXISTS fn_notifications_announcement_read_receipt;";
        await connection.ExecuteAsync(sql);
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer)
    {
        var sql = isSqlServer
            ? "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'fn_notifications_announcement_read_receipt'"
            : """
              SELECT COUNT(1)
              FROM information_schema.tables
              WHERE table_schema = DATABASE()
                AND table_name = 'fn_notifications_announcement_read_receipt'
              """;
        return await connection.ExecuteScalarAsync<int>(sql) > 0;
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%142_NotificationsAnnouncementReadReceipt.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%142_NotificationsAnnouncementReadReceipt.sql';
                  """);

    private static DbUpMigrationRunner CreateRunner(
        DatabaseProvider provider,
        string connectionString) =>
        new(
            Options.Create(new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
}
