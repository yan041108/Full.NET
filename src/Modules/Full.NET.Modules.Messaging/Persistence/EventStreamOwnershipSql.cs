using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

internal static class EventStreamOwnershipSql
{
    public static readonly SqlStatement FindByStream =
        new(
            "messaging.stream_ownership.find_by_stream",
            """
            SELECT MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                   CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                   Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                   CreatedAtUtc, UpdatedAtUtc
            FROM fn_messaging_stream_ownership
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListAll =
        new(
            "messaging.stream_ownership.list",
            """
            SELECT MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                   CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                   Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                   CreatedAtUtc, UpdatedAtUtc
            FROM fn_messaging_stream_ownership
            ORDER BY MessageType, SchemaVersion
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement Insert =
        new(
            "messaging.stream_ownership.insert",
            """
            INSERT INTO fn_messaging_stream_ownership
            (
                MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                CreatedAtUtc, UpdatedAtUtc
            )
            VALUES
            (
                @MessageType, @SchemaVersion, @TopicCode, @CurrentOwner, @PreviousOwner,
                @CutoffEventId, @CutoffOccurredAtUtc, @CdcSourcePositionJson, @OperatorUserId,
                @Reason, @RollbackBoundaryEventId, @RollbackOccurredAtUtc,
                @CreatedAtUtc, @UpdatedAtUtc
            )
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement Update =
        new(
            "messaging.stream_ownership.update",
            """
            UPDATE fn_messaging_stream_ownership
            SET TopicCode = @TopicCode,
                CurrentOwner = @CurrentOwner,
                PreviousOwner = @PreviousOwner,
                CutoffEventId = @CutoffEventId,
                CutoffOccurredAtUtc = @CutoffOccurredAtUtc,
                CdcSourcePositionJson = @CdcSourcePositionJson,
                OperatorUserId = @OperatorUserId,
                Reason = @Reason,
                RollbackBoundaryEventId = @RollbackBoundaryEventId,
                RollbackOccurredAtUtc = @RollbackOccurredAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindLastOutboxEventByStreamSqlServer =
        new(
            "messaging.outbox.find_last_by_stream.sqlserver",
            """
            SELECT TOP 1 Id AS CutoffEventId, OccurredAtUtc AS CutoffOccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC, Id DESC
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindLastOutboxEventByStreamMySql =
        new(
            "messaging.outbox.find_last_by_stream.mysql",
            """
            SELECT Id AS CutoffEventId, OccurredAtUtc AS CutoffOccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT 1
            """,
            SqlDataScope.Global);
}
