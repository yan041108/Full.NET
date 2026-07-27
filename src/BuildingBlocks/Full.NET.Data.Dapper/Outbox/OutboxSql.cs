using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

internal static class OutboxSql
{
    public static readonly SqlStatement ReadBacklogSqlServer = new(
        "outbox.read_backlog.sql_server",
        """
        SELECT COUNT_BIG(*) AS PendingCount,
               MIN(OccurredAtUtc) AS OldestOccurredAtUtc
        FROM fn_outbox_message
        WHERE ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadBacklogMySql = new(
        "outbox.read_backlog.my_sql",
        """
        SELECT COUNT(*) AS PendingCount,
               MIN(OccurredAtUtc) AS OldestOccurredAtUtc
        FROM fn_outbox_message
        WHERE ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadVersionRetirementSqlServer = new(
        "outbox.read_version_retirement.sql_server",
        """
        SELECT COUNT_BIG(
                   CASE WHEN DeadLetteredAtUtc IS NULL THEN 1 END
               ) AS PendingCount,
               COUNT_BIG(
                   CASE WHEN DeadLetteredAtUtc IS NOT NULL THEN 1 END
               ) AS DeadLetterCount,
               MIN(OccurredAtUtc) AS OldestUnprocessedOccurredAtUtc
        FROM fn_outbox_message
        WHERE ProcessedAtUtc IS NULL
          AND MessageType IN @MessageTypes
          AND SchemaVersion = @SchemaVersion;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadVersionRetirementMySql = new(
        "outbox.read_version_retirement.my_sql",
        """
        SELECT COALESCE(
                   SUM(CASE WHEN DeadLetteredAtUtc IS NULL THEN 1 ELSE 0 END),
                   0
               ) AS PendingCount,
               COALESCE(
                   SUM(CASE WHEN DeadLetteredAtUtc IS NOT NULL THEN 1 ELSE 0 END),
                   0
               ) AS DeadLetterCount,
               MIN(OccurredAtUtc) AS OldestUnprocessedOccurredAtUtc
        FROM fn_outbox_message
        WHERE ProcessedAtUtc IS NULL
          AND MessageType IN @MessageTypes
          AND SchemaVersion = @SchemaVersion;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireSqlServer = new(
        "outbox.acquire.sql_server",
        """
        ;WITH Pending AS
        (
            SELECT TOP (@BatchSize) *
            FROM fn_outbox_message WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
              AND (LockedUntilUtc IS NULL OR LockedUntilUtc <= @Now)
            ORDER BY OccurredAtUtc
        )
        UPDATE Pending
        SET LockId = @LockId,
            LockedUntilUtc = @LockedUntil,
            Attempts = Attempts + 1
        OUTPUT inserted.Id,
               inserted.LockId,
               inserted.MessageType,
               inserted.SchemaVersion,
               inserted.ContentType,
               inserted.TenantId,
               inserted.TraceId,
               inserted.Payload,
               inserted.Attempts,
               inserted.OccurredAtUtc;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireMySql = new(
        "outbox.acquire.my_sql",
        """
        UPDATE fn_outbox_message
        SET LockId = @LockId,
            LockedUntilUtc = @LockedUntil,
            Attempts = Attempts + 1
        WHERE ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
          AND (LockedUntilUtc IS NULL OR LockedUntilUtc <= @Now)
        ORDER BY OccurredAtUtc
        LIMIT @BatchSize;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectMySqlLease = new(
        "outbox.select_my_sql_lease",
        """
        SELECT Id,
               LockId,
               MessageType,
               SchemaVersion,
               ContentType,
               TenantId,
               TraceId,
               Payload,
               Attempts,
               OccurredAtUtc
        FROM fn_outbox_message
        WHERE LockId = @LockId
        ORDER BY OccurredAtUtc;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkProcessed = new(
        "outbox.mark_processed",
        """
        UPDATE fn_outbox_message
        SET ProcessedAtUtc = @Now,
            NextAttemptAtUtc = NULL,
            LockId = NULL,
            LockedUntilUtc = NULL,
            Error = NULL,
            DeadLetteredAtUtc = NULL,
            DeadLetterReasonCode = NULL
        WHERE Id = @Id
          AND LockId = @LockId
          AND ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "outbox.mark_failed",
        """
        UPDATE fn_outbox_message
        SET NextAttemptAtUtc = @NextAttemptAt,
            LockId = NULL,
            LockedUntilUtc = NULL,
            Error = @Error
        WHERE Id = @Id
          AND LockId = @LockId
          AND ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL;
    """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkDeadLetter = new(
        "outbox.mark_dead_letter",
        """
        UPDATE fn_outbox_message
        SET DeadLetteredAtUtc = @DeadLetteredAt,
            DeadLetterReasonCode = @DeadLetterReasonCode,
            NextAttemptAtUtc = NULL,
            LockId = NULL,
            LockedUntilUtc = NULL,
            Error = @Error
        WHERE Id = @Id
          AND LockId = @LockId
          AND ProcessedAtUtc IS NULL
          AND DeadLetteredAtUtc IS NULL;
        """,
        SqlDataScope.HostOnly);
}
