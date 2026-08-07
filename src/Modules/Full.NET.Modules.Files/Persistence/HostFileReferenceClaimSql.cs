using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

internal static class HostFileReferenceClaimSql
{
    public static readonly SqlStatement FindByIdempotencyKey = new(
        "files.file_reference_claim.find_by_idempotency_key",
        """
        SELECT Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId,
               State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc,
               ConfirmedAtUtc, ReleasedAtUtc
        FROM fn_files_file_reference_claim
        WHERE IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertPending = new(
        "files.file_reference_claim.insert_pending",
        """
        INSERT INTO fn_files_file_reference_claim
            (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId,
             State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @IdempotencyKey, @FileId, @ConsumerModule, @ConsumerReferenceId,
             @State, @ContentHash, @SizeBytes, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ConfirmPending = new(
        "files.file_reference_claim.confirm_pending",
        """
        UPDATE fn_files_file_reference_claim
        SET State = @ActiveState,
            ConfirmedAtUtc = @Now,
            UpdatedAtUtc = @Now
        WHERE IdempotencyKey = @IdempotencyKey
          AND State = @PendingState
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReleaseOpen = new(
        "files.file_reference_claim.release_open",
        """
        UPDATE fn_files_file_reference_claim
        SET State = @ReleasedState,
            ReleasedAtUtc = @Now,
            UpdatedAtUtc = @Now
        WHERE IdempotencyKey = @IdempotencyKey
          AND (State = @PendingState OR State = @ActiveState)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountOpenByFileId = new(
        "files.file_reference_claim.count_open_by_file_id",
        """
        SELECT COUNT(1)
        FROM fn_files_file_reference_claim
        WHERE FileId = @FileId
          AND (State = @PendingState OR State = @ActiveState)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectStalePendingSqlServer = new(
        "files.file_reference_claim.select_stale_pending.sql_server",
        """
        SELECT TOP (@BatchSize)
               Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId,
               State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc,
               ConfirmedAtUtc, ReleasedAtUtc
        FROM fn_files_file_reference_claim
        WHERE State = @PendingState
          AND UpdatedAtUtc <= @StaleBeforeUtc
          AND (@HasCursor = 0
               OR UpdatedAtUtc > @AfterUpdatedAtUtc
               OR (UpdatedAtUtc = @AfterUpdatedAtUtc AND Id > @AfterId))
        ORDER BY UpdatedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectStalePendingMySql = new(
        "files.file_reference_claim.select_stale_pending.mysql",
        """
        SELECT Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId,
               State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc,
               ConfirmedAtUtc, ReleasedAtUtc
        FROM fn_files_file_reference_claim
        WHERE State = @PendingState
          AND UpdatedAtUtc <= @StaleBeforeUtc
          AND (@HasCursor = 0
               OR UpdatedAtUtc > @AfterUpdatedAtUtc
               OR (UpdatedAtUtc = @AfterUpdatedAtUtc AND Id > @AfterId))
        ORDER BY UpdatedAtUtc, Id
        LIMIT @BatchSize
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PromoteToActive = new(
        "files.file_reference_claim.promote_to_active",
        """
        UPDATE fn_files_file_reference_claim
        SET State = @ActiveState,
            ConfirmedAtUtc = COALESCE(ConfirmedAtUtc, @Now),
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND State = @PendingState
        """,
        SqlDataScope.HostOnly);
}
