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
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version
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
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version
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
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version
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
            FROM fn_document_preview_task
            WHERE StatusKey = 'pending'
            ORDER BY CreatedAtUtc, Id
        )
        UPDATE task
        SET StatusKey = 'processing',
            StartedAtUtc = @Now,
            Version = task.Version + 1
        OUTPUT inserted.Id, inserted.DocumentItemId, inserted.VersionId, inserted.DocumentTitle,
               inserted.SourceFileId, inserted.SourceFileName, inserted.SourceMimeType,
               inserted.OutputFileId, inserted.StatusKey, inserted.ProviderKey, inserted.ErrorCode,
               inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.StartedAtUtc,
               inserted.CompletedAtUtc, inserted.Version
        FROM fn_document_preview_task AS task
        INNER JOIN candidates ON candidates.Id = task.Id;
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
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimByIdsMySql = new(
        "document.preview_task.claim_by_ids.mysql",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'processing',
            StartedAtUtc = @Now,
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
               CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version
        FROM fn_document_preview_task
        WHERE Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkSucceeded = new(
        "document.preview_task.mark_succeeded",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'succeeded',
            OutputFileId = @OutputFileId,
            ErrorCode = NULL,
            CompletedAtUtc = @CompletedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'processing'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "document.preview_task.mark_failed",
        """
        UPDATE fn_document_preview_task
        SET StatusKey = 'failed',
            ErrorCode = @ErrorCode,
            CompletedAtUtc = @CompletedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'processing'
        """,
        SqlDataScope.HostOnly);
}
