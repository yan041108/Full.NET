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
/// 验证 091 在 Outbox 表已存在但时间路径索引缺失或形状错误时无损收敛。
/// </summary>
[TestClass]
public sealed class Migration091MessagingOutboxInboxRecoveryTests
{
    private const string TimelineIndex =
        "IX_fn_messaging_outbox_event_OccurredAtUtc_Id";

    [TestMethod]
    public async Task SqlServer_messaging_outbox_migration_recovers_indexes_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var eventId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_messaging_outbox_event
                (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                 CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            VALUES
                (@Id, 'fullnet.messaging.outbox.recovery.event', 1,
                 'application/x-memorypack', NULL, @PartitionKey, NULL, NULL, NULL,
                 'fullnet.messaging.tests', 0x01, SYSDATETIMEOFFSET());
            DROP INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
                ON dbo.fn_messaging_outbox_event;
            CREATE CLUSTERED INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
                ON dbo.fn_messaging_outbox_event (PartitionKey);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%091_MessagingOutboxInboxExpand.sql';
            """,
            new
            {
                Id = eventId,
                PartitionKey = eventId.ToString("D"),
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_messaging_outbox_event"));
        await AssertSqlServerIndexAsync(connection, TimelineIndex, "OccurredAtUtc", "Id");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_messaging_outbox_migration_recovers_indexes_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var eventId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_messaging_outbox_event
                (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                 CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            VALUES
                (@Id, 'fullnet.messaging.outbox.recovery.event', 1,
                 'application/x-memorypack', NULL, @PartitionKey, NULL, NULL, NULL,
                 'fullnet.messaging.tests', 0x01, UTC_TIMESTAMP(6));
            DROP INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
                ON fn_messaging_outbox_event;
            CREATE INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
                ON fn_messaging_outbox_event (PartitionKey);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%091_MessagingOutboxInboxExpand.sql';
            """,
            new
            {
                Id = eventId,
                PartitionKey = eventId.ToString("D"),
            });

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_messaging_outbox_event"));
        await AssertMySqlIndexAsync(connection, TimelineIndex, "OccurredAtUtc", "Id");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task AssertSqlServerIndexAsync(
        SqlConnection connection,
        string indexName,
        string firstColumn,
        string secondColumn)
    {
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_messaging_outbox_event')
              AND indexObject.name = @IndexName
              AND indexObject.is_unique = 0
              AND indexObject.type = 1
              AND ((indexColumn.key_ordinal = 1 AND columnObject.name = @FirstColumn)
                   OR (indexColumn.key_ordinal = 2 AND columnObject.name = @SecondColumn))
            """,
            new
            {
                IndexName = indexName,
                FirstColumn = firstColumn,
                SecondColumn = secondColumn,
            }));
    }

    private static async Task AssertMySqlIndexAsync(
        MySqlConnection connection,
        string indexName,
        string firstColumn,
        string secondColumn)
    {
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_messaging_outbox_event'
              AND INDEX_NAME = @IndexName
              AND NON_UNIQUE = 1
              AND SUB_PART IS NULL
              AND ((SEQ_IN_INDEX = 1 AND COLUMN_NAME = @FirstColumn)
                   OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = @SecondColumn))
            """,
            new
            {
                IndexName = indexName,
                FirstColumn = firstColumn,
                SecondColumn = secondColumn,
            }));
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