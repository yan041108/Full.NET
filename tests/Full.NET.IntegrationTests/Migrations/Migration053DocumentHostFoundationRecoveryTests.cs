using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 楠岃瘉 053 鍦?Host 鏂囨。琛ㄥ凡瀛樺湪浣嗗垪琛ㄧ储寮曠己澶辨椂鍙棤鎹熸仮澶嶃€?/// </summary>
[TestClass]
public sealed class Migration053DocumentHostFoundationRecoveryTests
{
    private const string ListIndex = "IX_fn_document_item_HostList";

    [TestMethod]
    public async Task SqlServer_document_host_migration_recovers_list_index_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             INSERT INTO dbo.fn_document_item
                 (Id, TenantId, Title, IsDeleted, CreatedAtUtc, CreatedByUserId, Version)
             VALUES
                 (@ItemId, NULL, N'Host spec', 0, @Now, @UserId, 1);
             DROP INDEX {ListIndex} ON dbo.fn_document_item;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%053_DocumentHostFoundation.sql';
             """,
            new { ItemId = itemId, UserId = userId, Now = now });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_document_item WHERE Id = @ItemId",
            new { ItemId = itemId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.fn_document_item')
              AND name = @IndexName
            """,
            new { IndexName = ListIndex }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_document_host_migration_recovers_list_index_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.ExecuteAsync(
            $"""
             INSERT INTO fn_document_item
                 (Id, TenantId, Title, IsDeleted, CreatedAtUtc, CreatedByUserId, Version)
             VALUES
                 (@ItemId, NULL, 'Host spec', 0, @Now, @UserId, 1);
             DROP INDEX {ListIndex} ON fn_document_item;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%053_DocumentHostFoundation.sql';
             """,
            new { ItemId = itemId, UserId = userId, Now = now });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_document_item WHERE Id = @ItemId",
            new { ItemId = itemId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT INDEX_NAME) FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_document_item'
              AND INDEX_NAME = @IndexName
            """,
            new { IndexName = ListIndex }));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

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