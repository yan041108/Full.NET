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
            WHERE ProcessedAt IS NULL
              AND (NextAttemptAt IS NULL OR NextAttemptAt <= @Now)
              AND (LockedUntil IS NULL OR LockedUntil <= @Now)
            ORDER BY OccurredAt
        )
        UPDATE Pending
        SET LockId = @LockId,
            LockedUntil = @LockedUntil,
            Attempts = Attempts + 1
        OUTPUT inserted.Id,
               inserted.LockId,
               inserted.Type,
               inserted.SchemaVersion,
               inserted.ContentType,
               inserted.TenantId,
               inserted.TraceId,
               inserted.Payload,
               inserted.Attempts,
               inserted.OccurredAt;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireMySql = new(
        "outbox.acquire.my-sql",
        """
        UPDATE fn_outbox_message
        SET LockId = @LockId,
            LockedUntil = @LockedUntil,
            Attempts = Attempts + 1
        WHERE ProcessedAt IS NULL
          AND (NextAttemptAt IS NULL OR NextAttemptAt <= @Now)
          AND (LockedUntil IS NULL OR LockedUntil <= @Now)
        ORDER BY OccurredAt
        LIMIT @BatchSize;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectMySqlLease = new(
        "outbox.select-my-sql-lease",
        """
        SELECT Id,
               LockId,
               Type,
               SchemaVersion,
               ContentType,
               TenantId,
               TraceId,
               Payload,
               Attempts,
               OccurredAt
        FROM fn_outbox_message
        WHERE LockId = @LockId
        ORDER BY OccurredAt;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkProcessed = new(
        "outbox.mark-processed",
        """
        UPDATE fn_outbox_message
        SET ProcessedAt = @Now,
            LockId = NULL,
            LockedUntil = NULL,
            Error = NULL
        WHERE Id = @Id AND LockId = @LockId AND ProcessedAt IS NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "outbox.mark-failed",
        """
        UPDATE fn_outbox_message
        SET NextAttemptAt = @NextAttemptAt,
            LockId = NULL,
            LockedUntil = NULL,
            Error = @Error
        WHERE Id = @Id AND LockId = @LockId AND ProcessedAt IS NULL;
        """,
        SqlDataScope.HostOnly);
}
