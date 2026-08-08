using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

internal static class MessagingOperationsSql
{
    public static readonly SqlStatement CountDeadLetters =
        new(
            "messaging.dead_letters.count",
            """
            SELECT COUNT(*)
            FROM fn_messaging_inbox_message
            WHERE Status = 'failed'
              AND (@ConsumerName IS NULL OR ConsumerName = @ConsumerName)
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListDeadLettersSqlServer =
        new(
            "messaging.dead_letters.list.sql_server",
            """
            SELECT ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, Attempts,
                   ReceivedAtUtc, LastErrorCode, LastError
            FROM fn_messaging_inbox_message
            WHERE Status = 'failed'
              AND (@ConsumerName IS NULL OR ConsumerName = @ConsumerName)
            ORDER BY ReceivedAtUtc DESC, MessageId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListDeadLettersMySql =
        new(
            "messaging.dead_letters.list.mysql",
            """
            SELECT ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, Attempts,
                   ReceivedAtUtc, LastErrorCode, LastError
            FROM fn_messaging_inbox_message
            WHERE Status = 'failed'
              AND (@ConsumerName IS NULL OR ConsumerName = @ConsumerName)
            ORDER BY ReceivedAtUtc DESC, MessageId
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindDeadLetterByKey =
        new(
            "messaging.dead_letters.find_by_key",
            """
            SELECT ConsumerName, MessageId, MessageType, SchemaVersion, TenantId, Attempts,
                   ReceivedAtUtc, LastErrorCode, LastError
            FROM fn_messaging_inbox_message
            WHERE ConsumerName = @ConsumerName
              AND MessageId = @MessageId
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindOutboxEnvelopeById =
        new(
            "messaging.outbox.find_envelope_by_id",
            """
            SELECT Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                   CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE Id = @Id
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement CountDomainAuditRows =
        new(
            "messaging.domain_audit.count",
            """
            SELECT COUNT(*)
            FROM fn_messaging_domain_audit
            """,
            SqlDataScope.Global);
}