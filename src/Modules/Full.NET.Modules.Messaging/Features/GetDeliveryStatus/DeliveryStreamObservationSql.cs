using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

/// <summary>仅聚合 Messaging 自有源表、DLQ 投影与控制状态，不把保留源行数当作 Broker lag。</summary>
internal static class DeliveryStreamObservationSql
{
    internal static readonly SqlStatement ReadSqlServer = new("messaging.delivery_observation.read.sql_server", """
        WITH SourceCounts AS (
            SELECT MessageType, SchemaVersion, COUNT_BIG(*) AS RetainedCount FROM fn_messaging_outbox_event GROUP BY MessageType, SchemaVersion
        ), DeadLetterCounts AS (
            SELECT MessageType, SchemaVersion, COUNT_BIG(*) AS DeadLetterCount, MIN(FirstFailedAtUtc) AS OldestDeadLetterAtUtc
            FROM fn_messaging_dead_letter WHERE ReplayedAtUtc IS NULL AND MessageType IS NOT NULL AND SchemaVersion > 0
            GROUP BY MessageType, SchemaVersion
        ), StreamKeys AS (
            SELECT MessageType COLLATE Latin1_General_100_BIN2 AS MessageType, SchemaVersion FROM SourceCounts
            UNION SELECT MessageType COLLATE Latin1_General_100_BIN2, SchemaVersion FROM DeadLetterCounts
            UNION SELECT MessageType COLLATE Latin1_General_100_BIN2, SchemaVersion FROM fn_messaging_stream_ownership
        ), Observations AS (
            SELECT streams.MessageType, streams.SchemaVersion, COALESCE(source.RetainedCount, 0) AS RetainedEventCount,
                COALESCE(dlq.DeadLetterCount, 0) AS KafkaProjectedDeadLetterCount, dlq.OldestDeadLetterAtUtc,
                CAST(CASE WHEN ownership.MessageType IS NULL THEN 0 ELSE 1 END AS bigint) AS HasPersistedOwnership,
                CAST(COALESCE(ownership.RollbackState, 0) AS bigint) AS RollbackState, ownership.RollbackGeneration
            FROM StreamKeys streams
            LEFT JOIN SourceCounts source ON source.MessageType = streams.MessageType AND source.SchemaVersion = streams.SchemaVersion
            LEFT JOIN DeadLetterCounts dlq ON dlq.MessageType = streams.MessageType AND dlq.SchemaVersion = streams.SchemaVersion
            LEFT JOIN fn_messaging_stream_ownership ownership ON ownership.MessageType COLLATE Latin1_General_100_BIN2 = streams.MessageType AND ownership.SchemaVersion = streams.SchemaVersion
            UNION ALL
            SELECT NULL, NULL, 0, COUNT_BIG(*), NULL, 0, 0, NULL
            FROM fn_messaging_dead_letter WHERE ReplayedAtUtc IS NULL AND (MessageType IS NULL OR SchemaVersion IS NULL OR SchemaVersion <= 0)
        )
        SELECT TOP (@MaxStreams) MessageType, SchemaVersion, RetainedEventCount, KafkaProjectedDeadLetterCount,
            OldestDeadLetterAtUtc, HasPersistedOwnership, RollbackState, RollbackGeneration
        FROM Observations ORDER BY MessageType, SchemaVersion
        """, SqlDataScope.Global);
    internal static readonly SqlStatement ReadMySql = new("messaging.delivery_observation.read.my_sql", """
        WITH SourceCounts AS (
            SELECT MessageType, SchemaVersion, CAST(COUNT(*) AS SIGNED) AS RetainedCount FROM fn_messaging_outbox_event GROUP BY MessageType, SchemaVersion
        ), DeadLetterCounts AS (
            SELECT MessageType, SchemaVersion, CAST(COUNT(*) AS SIGNED) AS DeadLetterCount, MIN(FirstFailedAtUtc) AS OldestDeadLetterAtUtc
            FROM fn_messaging_dead_letter WHERE ReplayedAtUtc IS NULL AND MessageType IS NOT NULL AND SchemaVersion > 0
            GROUP BY MessageType, SchemaVersion
        ), StreamKeys AS (
            SELECT MessageType, SchemaVersion FROM SourceCounts
            UNION SELECT MessageType, SchemaVersion FROM DeadLetterCounts
            UNION SELECT MessageType, SchemaVersion FROM fn_messaging_stream_ownership
        ), Observations AS (
            SELECT streams.MessageType, streams.SchemaVersion, COALESCE(source.RetainedCount, 0) AS RetainedEventCount,
                COALESCE(dlq.DeadLetterCount, 0) AS KafkaProjectedDeadLetterCount, dlq.OldestDeadLetterAtUtc,
                CAST(CASE WHEN ownership.MessageType IS NULL THEN 0 ELSE 1 END AS SIGNED) AS HasPersistedOwnership,
                CAST(COALESCE(ownership.RollbackState, 0) AS SIGNED) AS RollbackState, ownership.RollbackGeneration
            FROM StreamKeys streams
            LEFT JOIN SourceCounts source ON source.MessageType = streams.MessageType AND source.SchemaVersion = streams.SchemaVersion
            LEFT JOIN DeadLetterCounts dlq ON dlq.MessageType = streams.MessageType AND dlq.SchemaVersion = streams.SchemaVersion
            LEFT JOIN fn_messaging_stream_ownership ownership ON ownership.MessageType = streams.MessageType AND ownership.SchemaVersion = streams.SchemaVersion
            UNION ALL
            SELECT NULL, NULL, 0, CAST(COUNT(*) AS SIGNED), NULL, 0, 0, NULL
            FROM fn_messaging_dead_letter WHERE ReplayedAtUtc IS NULL AND (MessageType IS NULL OR SchemaVersion IS NULL OR SchemaVersion <= 0)
        )
        SELECT MessageType, SchemaVersion, RetainedEventCount, KafkaProjectedDeadLetterCount,
            OldestDeadLetterAtUtc, HasPersistedOwnership, RollbackState, RollbackGeneration
        FROM Observations ORDER BY MessageType, SchemaVersion LIMIT @MaxStreams
        """, SqlDataScope.Global);
}

/// <summary>有界分组投影；空事件键的单行专门统计无法归属事件流的 DLQ。</summary>
internal sealed record DeliveryStreamObservationRow(string? MessageType, int? SchemaVersion, long RetainedEventCount,
    long KafkaProjectedDeadLetterCount, DateTimeOffset? OldestDeadLetterAtUtc, long HasPersistedOwnership, long RollbackState, Guid? RollbackGeneration);
