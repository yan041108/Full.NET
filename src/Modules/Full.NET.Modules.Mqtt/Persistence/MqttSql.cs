using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Mqtt.Persistence;

internal static class MqttSql
{
    private const string ClientColumns =
        """
        Id, ClientKey, DisplayName, Description, TenantId,
        IsEnabled, SortOrder, CreatedAtUtc, UpdatedAtUtc
        """;

    private const string MessageColumns =
        """
        message.Id, message.TenantId, message.ClientId, client.ClientKey,
        message.Topic, message.PayloadSizeBytes, message.Qos, message.Status,
        message.IdempotencyKey, message.SummaryMessage, message.PublishedAtUtc,
        message.CreatedAtUtc, message.CreatedByUserId
        """;

    private const string MessageFromClause =
        """
        FROM fn_mqtt_message AS message
        LEFT JOIN fn_mqtt_client AS client ON client.Id = message.ClientId
        """;

    private const string MessageWhereClause =
        """
        (@ClientId IS NULL OR message.ClientId = @ClientId)
          AND (@Status IS NULL OR message.Status = @Status)
          AND (@Topic IS NULL OR message.Topic LIKE @TopicPattern)
          AND (@FromUtc IS NULL OR message.CreatedAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR message.CreatedAtUtc <= @ToUtc)
        """;

    public static readonly SqlStatement ListClients =
        new(
            "mqtt.list_clients",
            $"""
            SELECT {ClientColumns}
            FROM fn_mqtt_client
            ORDER BY SortOrder, ClientKey
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindClientById =
        new(
            "mqtt.find_client_by_id",
            $"""
            SELECT {ClientColumns}
            FROM fn_mqtt_client
            WHERE Id = @Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMessagesSqlServer =
        new(
            "mqtt.count_messages.sql_server",
            $"""
            SELECT COUNT(1)
            {MessageFromClause}
            WHERE {MessageWhereClause}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMessagesMySql =
        new(
            "mqtt.count_messages.mysql",
            $"""
            SELECT COUNT(1)
            {MessageFromClause}
            WHERE {MessageWhereClause}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMessagesSqlServer =
        new(
            "mqtt.list_messages.sql_server",
            $"""
            SELECT {MessageColumns}
            {MessageFromClause}
            WHERE {MessageWhereClause}
            ORDER BY message.CreatedAtUtc DESC, message.Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMessagesMySql =
        new(
            "mqtt.list_messages.mysql",
            $"""
            SELECT {MessageColumns}
            {MessageFromClause}
            WHERE {MessageWhereClause}
            ORDER BY message.CreatedAtUtc DESC, message.Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindMessageById =
        new(
            "mqtt.find_message_by_id",
            $"""
            SELECT {MessageColumns}
            {MessageFromClause}
            WHERE message.Id = @Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindMessageByIdempotency =
        new(
            "mqtt.find_message_by_idempotency",
            $"""
            SELECT {MessageColumns}
            {MessageFromClause}
            WHERE message.TenantId = @TenantId
              AND message.IdempotencyKey = @IdempotencyKey
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertMessage =
        new(
            "mqtt.insert_message",
            """
            INSERT INTO fn_mqtt_message
                (Id, TenantId, ClientId, Topic, PayloadSizeBytes, Qos, Status,
                 IdempotencyKey, SummaryMessage, PublishedAtUtc, CreatedAtUtc, CreatedByUserId)
            VALUES
                (@Id, @TenantId, @ClientId, @Topic, @PayloadSizeBytes, @Qos, @Status,
                 @IdempotencyKey, @SummaryMessage, @PublishedAtUtc, @CreatedAtUtc, @CreatedByUserId)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateMessageStatus =
        new(
            "mqtt.update_message_status",
            """
            UPDATE fn_mqtt_message
            SET Status = @Status,
                SummaryMessage = @SummaryMessage,
                PublishedAtUtc = @PublishedAtUtc
            WHERE Id = @Id
            """,
            SqlDataScope.HostOnly);
}
