using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>
/// 跨模块文件引用 Claim 状态机的参数化 SQL 集合，全部声明 <c>SqlDataScope.HostOnly</c>。
/// </summary>
/// <remarks>
/// 状态机：<c>pending -> active -> released</c>。<see cref="InsertPending"/> 以子查询校验目标文件处于 <c>ready</c> 且未软删除后才插入 Claim；
/// <see cref="ConfirmPending"/> 与 <see cref="ReleaseOpen"/> 按 <c>IdempotencyKey</c> 推进状态，保证幂等；
/// <see cref="CountOpenByFileId"/> 为软删除守卫提供引用计数，未释放的 Claim 阻止 Blob 回收。
/// 陈旧 <c>pending</c> Claim 由对账 Runner 按配置阈值晋升或回收，避免上传中断后引用永久阻塞清理。
/// </remarks>
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
        SELECT @Id, @IdempotencyKey, @FileId, @ConsumerModule, @ConsumerReferenceId,
               @State, @ContentHash, @SizeBytes, @CreatedAtUtc, @UpdatedAtUtc
        FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND StorageState = 'ready'
          AND DeletedAtUtc IS NULL
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
