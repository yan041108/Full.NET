using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 鍙屽簱 Messaging Outbox/Inbox 琛ㄧ粨鏋勫绾︽柇瑷€锛屼緵 Provider 澶瑰叿澶嶇敤銆?/// </summary>
internal static class MessagingOutboxSchemaAssertions
{
    private static readonly string[] ForbiddenOutboxPollingColumns =
    [
        "ProcessedAtUtc",
        "Attempts",
        "LockId",
        "LockedUntilUtc",
        "NextAttemptAtUtc",
    ];

    public static async Task VerifySqlServerAsync(SqlConnection connection)
    {
        await VerifyOutboxEventTableSqlServerAsync(connection);
        await VerifyInboxMessageTableSqlServerAsync(connection);
        await VerifyOutboxTimelineIndexSqlServerAsync(connection);
    }

    public static async Task VerifyMySqlAsync(MySqlConnection connection)
    {
        await VerifyOutboxEventTableMySqlAsync(connection);
        await VerifyInboxMessageTableMySqlAsync(connection);
        await VerifyOutboxTimelineIndexMySqlAsync(connection);
    }

    private static async Task VerifyOutboxEventTableSqlServerAsync(SqlConnection connection)
    {
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                """));

        foreach (var column in ForbiddenOutboxPollingColumns)
        {
            Assert.AreEqual(
                0,
                await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = 'dbo'
                      AND TABLE_NAME = 'fn_messaging_outbox_event'
                      AND COLUMN_NAME = @ColumnName
                    """,
                    new { ColumnName = column }));
        }

        Assert.AreEqual(
            12,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                  AND COLUMN_NAME = 'Id'
                  AND DATA_TYPE = 'uniqueidentifier'
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.key_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.fn_messaging_outbox_event')
                  AND name = 'PK_fn_messaging_outbox_event'
                  AND type = 'PK'
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes AS indexObject
                INNER JOIN sys.key_constraints AS keyObject
                    ON keyObject.parent_object_id = indexObject.object_id
                   AND keyObject.unique_index_id = indexObject.index_id
                WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_messaging_outbox_event')
                  AND keyObject.name = 'PK_fn_messaging_outbox_event'
                  AND indexObject.type = 2
                """));
    }

    private static async Task VerifyInboxMessageTableSqlServerAsync(SqlConnection connection)
    {
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                """));

        Assert.AreEqual(
            12,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                """));

        Assert.AreEqual(
            2,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                  AND CONSTRAINT_NAME = 'PK_fn_messaging_inbox_message'
                  AND COLUMN_NAME IN ('ConsumerName', 'MessageId')
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                  AND COLUMN_NAME = 'PayloadHash'
                  AND DATA_TYPE = 'varbinary'
                  AND CHARACTER_MAXIMUM_LENGTH = 32
                """));
    }

    private static async Task VerifyOutboxTimelineIndexSqlServerAsync(SqlConnection connection)
    {
        Assert.AreEqual(
            2,
            await connection.ExecuteScalarAsync<int>(
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
                  AND indexObject.name = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
                  AND indexObject.is_unique = 0
                  AND indexObject.type = 1
                  AND ((indexColumn.key_ordinal = 1 AND columnObject.name = 'OccurredAtUtc')
                       OR (indexColumn.key_ordinal = 2 AND columnObject.name = 'Id'))
                """));
    }

    private static async Task VerifyOutboxEventTableMySqlAsync(MySqlConnection connection)
    {
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                """));

        foreach (var column in ForbiddenOutboxPollingColumns)
        {
            Assert.AreEqual(
                0,
                await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'fn_messaging_outbox_event'
                      AND COLUMN_NAME = @ColumnName
                    """,
                    new { ColumnName = column }));
        }

        Assert.AreEqual(
            12,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                  AND COLUMN_NAME = 'Id'
                  AND DATA_TYPE = 'binary'
                  AND CHARACTER_MAXIMUM_LENGTH = 16
                """));
    }

    private static async Task VerifyInboxMessageTableMySqlAsync(MySqlConnection connection)
    {
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                """));

        Assert.AreEqual(
            12,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                """));

        Assert.AreEqual(
            2,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                  AND CONSTRAINT_NAME = 'PRIMARY'
                  AND COLUMN_NAME IN ('ConsumerName', 'MessageId')
                """));

        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_inbox_message'
                  AND COLUMN_NAME = 'PayloadHash'
                  AND DATA_TYPE = 'binary'
                  AND CHARACTER_MAXIMUM_LENGTH = 32
                """));
    }

    private static async Task VerifyOutboxTimelineIndexMySqlAsync(MySqlConnection connection)
    {
        Assert.AreEqual(
            2,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_messaging_outbox_event'
                  AND INDEX_NAME = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
                  AND NON_UNIQUE = 1
                  AND SUB_PART IS NULL
                  AND ((SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'OccurredAtUtc')
                       OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'Id'))
                """));
    }
}
