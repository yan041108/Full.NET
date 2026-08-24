using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native Kafka Replay E2E 的数据库前置：为测试事件流登记 CDC Kafka 所有权。
/// </summary>
internal static class NativeApiKafkaReplaySupport
{
    internal static async Task EnsureCdcKafkaOwnershipAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            MessageType = Messaging.MessagingOutboxTestSupport.TestEventType,
            SchemaVersion = Messaging.MessagingOutboxTestSupport.TestSchemaVersion,
            TopicCode = Messaging.MessagingInboxTestSupport.TopicCode,
            Reason = "native kafka replay e2e",
        };

        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.fn_messaging_stream_ownership
                    WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion)
                BEGIN
                    INSERT INTO dbo.fn_messaging_stream_ownership
                        (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                         CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
                    VALUES
                        (@MessageType, @SchemaVersion, @TopicCode, 2, 0,
                         '00000000-0000-0000-0000-000000000000', SYSDATETIMEOFFSET(),
                         @Reason, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                END
                """,
                parameters);
            return;
        }

        await using var mySqlConnection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await mySqlConnection.ExecuteAsync(
            """
            INSERT IGNORE INTO fn_messaging_stream_ownership
                (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                 CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@MessageType, @SchemaVersion, @TopicCode, 2, 0,
                 0x00000000000000000000000000000000, UTC_TIMESTAMP(6),
                 @Reason, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """,
            parameters);
    }
}
