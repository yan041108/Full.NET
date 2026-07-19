using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

internal static class OutboxSql
{
    public static readonly SqlStatement AcquireSqlServer = new(
        "outbox.acquire.sql-server",
        """
        ;WITH Pending AS
        (
            SELECT TOP (@BatchSize) *
            FROM fn_outbox_message WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL
              AND (COALESCE(NextAttemptAtUtc, NextAttemptAt) IS NULL
                   OR COALESCE(NextAttemptAtUtc, NextAttemptAt) <= @Now)
              AND (COALESCE(LockedUntilUtc, LockedUntil) IS NULL
                   OR COALESCE(LockedUntilUtc, LockedUntil) <= @Now)
            ORDER BY COALESCE(OccurredAtUtc, OccurredAt)
        )
        UPDATE Pending
        SET LockId = @LockId,
            LockedUntilUtc = @LockedUntil,
            Attempts = Attempts + 1
        OUTPUT inserted.Id,
               inserted.LockId,
               COALESCE(inserted.MessageType, inserted.Type) AS MessageType,
               inserted.SchemaVersion,
               inserted.ContentType,
               inserted.TenantId,
               inserted.TraceId,
               inserted.Payload,
               inserted.Attempts,
               COALESCE(inserted.OccurredAtUtc, inserted.OccurredAt) AS OccurredAtUtc;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireMySql = new(
        "outbox.acquire.my-sql",
        """
        UPDATE fn_outbox_message
        SET LockId = @LockId,
            LockedUntilUtc = @LockedUntil,
            Attempts = Attempts + 1
        WHERE COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL
          AND (COALESCE(NextAttemptAtUtc, NextAttemptAt) IS NULL
               OR COALESCE(NextAttemptAtUtc, NextAttemptAt) <= @Now)
          AND (COALESCE(LockedUntilUtc, LockedUntil) IS NULL
               OR COALESCE(LockedUntilUtc, LockedUntil) <= @Now)
        ORDER BY COALESCE(OccurredAtUtc, OccurredAt)
        LIMIT @BatchSize;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectMySqlLease = new(
        "outbox.select-my-sql-lease",
        """
        SELECT Id,
               LockId,
               COALESCE(MessageType, Type) AS MessageType,
               SchemaVersion,
               ContentType,
               TenantId,
               TraceId,
               Payload,
               Attempts,
               COALESCE(OccurredAtUtc, OccurredAt) AS OccurredAtUtc
        FROM fn_outbox_message
        WHERE LockId = @LockId
        ORDER BY COALESCE(OccurredAtUtc, OccurredAt);
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkProcessed = new(
        "outbox.mark-processed",
        """
        UPDATE fn_outbox_message
        SET ProcessedAtUtc = @Now,
            LockId = NULL,
            LockedUntilUtc = NULL,
            Error = NULL
        WHERE Id = @Id
          AND LockId = @LockId
          AND COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "outbox.mark-failed",
        """
        UPDATE fn_outbox_message
        SET NextAttemptAtUtc = @NextAttemptAt,
            LockId = NULL,
            LockedUntilUtc = NULL,
            Error = @Error
        WHERE Id = @Id
          AND LockId = @LockId
          AND COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL;
        """,
        SqlDataScope.HostOnly);
}
