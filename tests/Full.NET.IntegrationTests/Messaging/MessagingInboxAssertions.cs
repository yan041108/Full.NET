using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 双库 Messaging Inbox 断言，供 Provider 夹具复用。
/// </summary>
internal static class MessagingInboxAssertions
{
    public static async Task AssertInboxStatusSqlServerAsync(
        SqlConnection connection,
        string consumerName,
        Guid messageId,
        string expectedStatus)
    {
        var status = await connection.ExecuteScalarAsync<string>(
            """
            SELECT Status
            FROM dbo.fn_messaging_inbox_message
            WHERE ConsumerName = @ConsumerName
              AND MessageId = @MessageId
            """,
            new { ConsumerName = consumerName, MessageId = messageId });

        Assert.AreEqual(expectedStatus, status);
    }

    public static async Task AssertInboxCountSqlServerAsync(
        SqlConnection connection,
        string consumerName,
        Guid messageId,
        int expectedCount)
    {
        Assert.AreEqual(
            expectedCount,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.fn_messaging_inbox_message
                WHERE ConsumerName = @ConsumerName
                  AND MessageId = @MessageId
                """,
                new { ConsumerName = consumerName, MessageId = messageId }));
    }

    public static async Task AssertDownstreamOutboxCountSqlServerAsync(
        SqlConnection connection,
        string partitionKey,
        int expectedCount)
    {
        Assert.AreEqual(
            expectedCount,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.fn_messaging_outbox_event
                WHERE PartitionKey = @PartitionKey
                """,
                new { PartitionKey = partitionKey }));
    }

    public static async Task AssertInboxStatusMySqlAsync(
        MySqlConnection connection,
        string consumerName,
        Guid messageId,
        string expectedStatus)
    {
        var status = await connection.ExecuteScalarAsync<string>(
            """
            SELECT Status
            FROM fn_messaging_inbox_message
            WHERE ConsumerName = @ConsumerName
              AND MessageId = @MessageId
            """,
            new { ConsumerName = consumerName, MessageId = messageId });

        Assert.AreEqual(expectedStatus, status);
    }

    public static async Task AssertInboxCountMySqlAsync(
        MySqlConnection connection,
        string consumerName,
        Guid messageId,
        int expectedCount)
    {
        Assert.AreEqual(
            expectedCount,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM fn_messaging_inbox_message
                WHERE ConsumerName = @ConsumerName
                  AND MessageId = @MessageId
                """,
                new { ConsumerName = consumerName, MessageId = messageId }));
    }

    public static async Task AssertDownstreamOutboxCountMySqlAsync(
        MySqlConnection connection,
        string partitionKey,
        int expectedCount)
    {
        Assert.AreEqual(
            expectedCount,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM fn_messaging_outbox_event
                WHERE PartitionKey = @PartitionKey
                """,
                new { PartitionKey = partitionKey }));
    }
}