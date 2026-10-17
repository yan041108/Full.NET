using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档预览转换任务 SQL 语句。</summary>
internal static class DocumentPreviewTaskSql
{
    public static readonly SqlStatement Insert = new(
        "document.preview_task.insert",
        """
        INSERT INTO fn_document_preview_task
            (Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
             OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
             CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version)
        VALUES
            (@Id, @DocumentItemId, @VersionId, @DocumentTitle, @SourceFileId, @SourceFileName, @SourceMimeType,
             @OutputFileId, @StatusKey, @ProviderKey, @ErrorCode, @RequestedByUserId,
             @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "document.preview_task.find_by_id",
        """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageSqlServer = new(
        "document.preview_task.page.sqlserver",
        """
        SELECT COUNT(1)
        FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId);

        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "document.preview_task.page.mysql",
        """
        SELECT COUNT(1)
        FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId);

        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimPendingSqlServer = new(
        "document.preview_task.claim.sqlserver",
        """
        WITH candidates AS (
            SELECT TOP (@BatchSize) Id
            FROM fn_document_preview_task WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE StatusKey = 'pending'
            ORDER BY CreatedAtUtc, Id
        )
        UPDATE task
        SET StatusKey = 'processing',
            StartedAtUtc = @Now,
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = task.Version + 1
        OUTPUT inserted.Id, inserted.DocumentItemId, inserted.VersionId, inserted.DocumentTitle,
               inserted.SourceFileId, inserted.SourceFileName, inserted.SourceMimeType,
               inserted.OutputFileId, inserted.StatusKey, inserted.ProviderKey, inserted.ErrorCode,
               inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.StartedAtUtc,
               inserted.CompletedAtUtc, inserted.Version, inserted.LeaseId, inserted.LeaseExpiresAtUtc
        FROM fn_document_preview_task AS task
        INNER JOIN candidates ON candidates.Id = task.Id
        WHERE task.StatusKey = 'pending';
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectClaimableIdsMySql = new(
        "document.preview_task.select_claimable_ids.mysql",
        """
        SELECT Id
        FROM fn_document_preview_task
        WHERE StatusKey = 'pending'
        ORDER BY CreatedAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimByIdsMySql = new(
        "document.preview_task.claim_by_ids.mysql",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'processing',
            StartedAtUtc = @Now,
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1
        WHERE StatusKey = 'pending'
          AND Id IN @Ids
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectByIds = new(
        "document.preview_task.select_by_ids",
        """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task
        WHERE Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>先锁定完成主行，再由调用方读取当前时间；不能用等待锁之前的时间判断租约。</summary>
    public static readonly SqlStatement LockCompletionSqlServer = new(
        "document.preview_task.lock_completion.sqlserver",
        """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @Id
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement LockCompletionMySql = new(
        "document.preview_task.lock_completion.mysql",
        """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType,
               OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId,
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, LeaseId, LeaseExpiresAtUtc
        FROM fn_document_preview_task WHERE Id = @Id FOR UPDATE
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkSucceeded = new(
        "document.preview_task.mark_succeeded",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'succeeded',
            OutputFileId = @OutputFileId,
            ErrorCode = NULL,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId AND LeaseExpiresAtUtc > @Now
        """,
        SqlDataScope.HostOnly);

    /// <summary>仅取消创建人未领取的任务，按版本与领取竞争，不修改源文件或结果引用。</summary>
    public static readonly SqlStatement Cancel = new(
        "document.preview_task.cancel",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'cancelled', CompletedAtUtc = @CompletedAtUtc, Version = Version + 1
        WHERE Id = @Id AND RequestedByUserId = @RequestedByUserId
          AND StatusKey = 'pending' AND Version = @ExpectedVersion
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "document.preview_task.mark_failed",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'failed',
            ErrorCode = @ErrorCode,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId AND LeaseExpiresAtUtc > @Now
        """,
        SqlDataScope.HostOnly);

    /// <summary>有界收口失效处理；终态不自动重排，旧版本 Worker 也不能覆盖 failed。</summary>
    public static readonly SqlStatement ExpireProcessingSqlServer = new(
        "document.preview_task.expire_processing.sqlserver",
        """
        WITH candidates AS (
            SELECT TOP (@BatchSize) Id
            FROM fn_document_preview_task WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE StatusKey = 'processing'
              AND ((LeaseExpiresAtUtc IS NOT NULL AND LeaseExpiresAtUtc <= @Now)
                OR (LeaseExpiresAtUtc IS NULL AND COALESCE(StartedAtUtc, CreatedAtUtc) <= @LegacyCutoff))
            ORDER BY LeaseExpiresAtUtc, StartedAtUtc, CreatedAtUtc, Id
        )
        UPDATE task SET StatusKey = 'failed', ErrorCode = @ErrorCode, CompletedAtUtc = @Now,
            LeaseId = NULL, LeaseExpiresAtUtc = NULL, Version = task.Version + 1
        FROM fn_document_preview_task AS task INNER JOIN candidates ON candidates.Id = task.Id
        WHERE task.StatusKey = 'processing'
          AND ((task.LeaseExpiresAtUtc IS NOT NULL AND task.LeaseExpiresAtUtc <= @Now)
            OR (task.LeaseExpiresAtUtc IS NULL AND COALESCE(task.StartedAtUtc, task.CreatedAtUtc) <= @LegacyCutoff));
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement ExpireProcessingMySql = new(
        "document.preview_task.expire_processing.mysql",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'failed', ErrorCode = @ErrorCode, CompletedAtUtc = @Now,
            LeaseId = NULL, LeaseExpiresAtUtc = NULL, Version = Version + 1
        WHERE StatusKey = 'processing'
          AND ((LeaseExpiresAtUtc IS NOT NULL AND LeaseExpiresAtUtc <= @Now)
            OR (LeaseExpiresAtUtc IS NULL AND COALESCE(StartedAtUtc, CreatedAtUtc) <= @LegacyCutoff))
        ORDER BY LeaseExpiresAtUtc, StartedAtUtc, CreatedAtUtc, Id
        LIMIT @BatchSize;
        """, SqlDataScope.HostOnly);
}
