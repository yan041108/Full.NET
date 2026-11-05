using Full.NET.Data.Abstractions;
namespace Full.NET.Modules.Document.Persistence;
/// <summary>预览任务领取、退避及终态使用同一领取版本，旧所有者无法提交。</summary>
internal static class DocumentPreviewTaskSql
{
    internal const string Columns = "Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc";
    internal static readonly SqlStatement Insert = new("document.preview_task.insert", """
        INSERT INTO fn_document_preview_task (Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc)
        VALUES (@Id, @DocumentItemId, @VersionId, @DocumentTitle, @SourceFileId, @SourceFileName, @SourceMimeType, @OutputFileId, @StatusKey, @ProviderKey, @ErrorCode, @RequestedByUserId, @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc, @Version, @SourceReferenceId, @OutputReferenceId, @OutputOwnerId, @AttemptCount, @NextAttemptAtUtc, @LeaseExpiresAtUtc)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindById = new("document.preview_task.find_by_id", """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc FROM fn_document_preview_task WHERE Id = @Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement PageSqlServer = new("document.preview_task.page.sqlserver", $$"""
        SELECT COUNT(1) FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        AND {{DocumentPermissionSql.RelatedReadPredicate("fn_document_preview_task.DocumentItemId")}};
        SELECT {{Columns}} FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        AND {{DocumentPermissionSql.RelatedReadPredicate("fn_document_preview_task.DocumentItemId")}}
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement PageMySql = new("document.preview_task.page.mysql", $$"""
        SELECT COUNT(1) FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        AND {{DocumentPermissionSql.RelatedReadPredicate("fn_document_preview_task.DocumentItemId")}};
        SELECT {{Columns}} FROM fn_document_preview_task
        WHERE (@DocumentItemId IS NULL OR DocumentItemId = @DocumentItemId)
        AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        AND {{DocumentPermissionSql.RelatedReadPredicate("fn_document_preview_task.DocumentItemId")}}
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ClaimPendingSqlServer = new("document.preview_task.claim.sqlserver", """
        WITH candidates AS (
        SELECT TOP (@BatchSize) Id FROM fn_document_preview_task WITH (UPDLOCK, ROWLOCK, READPAST)
        WHERE ((StatusKey = 'pending' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)) OR (StatusKey = 'processing' AND ((LeaseExpiresAtUtc IS NOT NULL AND LeaseExpiresAtUtc <= @Now) OR (LeaseExpiresAtUtc IS NULL AND StartedAtUtc <= @ExpiredBefore)))) AND SourceReferenceId IS NOT NULL ORDER BY CreatedAtUtc, Id)
        UPDATE task SET StatusKey = CASE WHEN task.AttemptCount >= @MaxAttempts THEN 'failed' ELSE 'processing' END,
        ErrorCode = CASE WHEN task.AttemptCount >= @MaxAttempts THEN 'document.preview.attempts_exhausted' ELSE NULL END,
        CompletedAtUtc = CASE WHEN task.AttemptCount >= @MaxAttempts THEN @Now ELSE NULL END,
        StartedAtUtc = @Now, LeaseExpiresAtUtc = @LeaseExpiresAtUtc, AttemptCount = task.AttemptCount + CASE WHEN task.AttemptCount >= @MaxAttempts THEN 0 ELSE 1 END, Version = task.Version + 1
        OUTPUT inserted.Id, inserted.DocumentItemId, inserted.VersionId, inserted.DocumentTitle, inserted.SourceFileId, inserted.SourceFileName, inserted.SourceMimeType, inserted.OutputFileId, inserted.StatusKey, inserted.ProviderKey, inserted.ErrorCode, inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.StartedAtUtc, inserted.CompletedAtUtc, inserted.Version, inserted.SourceReferenceId, inserted.OutputReferenceId, inserted.OutputOwnerId, inserted.AttemptCount, inserted.NextAttemptAtUtc, inserted.LeaseExpiresAtUtc
        FROM fn_document_preview_task AS task INNER JOIN candidates ON candidates.Id = task.Id;
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement SelectClaimableIdsMySql = new("document.preview_task.select_claimable_ids.mysql", """
        SELECT Id FROM fn_document_preview_task WHERE ((StatusKey = 'pending' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)) OR (StatusKey = 'processing' AND ((LeaseExpiresAtUtc IS NOT NULL AND LeaseExpiresAtUtc <= @Now) OR (LeaseExpiresAtUtc IS NULL AND StartedAtUtc <= @ExpiredBefore)))) AND SourceReferenceId IS NOT NULL
        ORDER BY CreatedAtUtc, Id LIMIT @BatchSize FOR UPDATE SKIP LOCKED
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ClaimByIdsMySql = new("document.preview_task.claim_by_ids.mysql", """
        UPDATE fn_document_preview_task SET StatusKey = CASE WHEN AttemptCount >= @MaxAttempts THEN 'failed' ELSE 'processing' END,
        ErrorCode = CASE WHEN AttemptCount >= @MaxAttempts THEN 'document.preview.attempts_exhausted' ELSE NULL END,
        CompletedAtUtc = CASE WHEN AttemptCount >= @MaxAttempts THEN @Now ELSE NULL END,
        StartedAtUtc = @Now, LeaseExpiresAtUtc = @LeaseExpiresAtUtc, AttemptCount = AttemptCount + CASE WHEN AttemptCount >= @MaxAttempts THEN 0 ELSE 1 END, Version = Version + 1
        WHERE Id IN @Ids AND ((StatusKey = 'pending' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)) OR (StatusKey = 'processing' AND ((LeaseExpiresAtUtc IS NOT NULL AND LeaseExpiresAtUtc <= @Now) OR (LeaseExpiresAtUtc IS NULL AND StartedAtUtc <= @ExpiredBefore)))) AND SourceReferenceId IS NOT NULL
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement SelectByIds = new("document.preview_task.select_by_ids", """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc FROM fn_document_preview_task WHERE Id IN @Ids ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement RenewLease = new("document.preview_task.renew_lease", """
        UPDATE fn_document_preview_task SET StartedAtUtc = @Now, LeaseExpiresAtUtc = @LeaseExpiresAtUtc WHERE Id = @Id AND Version = @Version AND StatusKey = 'processing' AND LeaseExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MakeRunnable = new("document.preview_task.make_runnable", """
        UPDATE fn_document_preview_task SET StatusKey = 'pending', Version = Version + 1 WHERE Id = @Id AND StatusKey = 'preparing'
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkSucceeded = new("document.preview_task.mark_succeeded", """
        UPDATE fn_document_preview_task SET StatusKey = 'succeeded', OutputFileId = @OutputFileId, ErrorCode = NULL, CompletedAtUtc = @CompletedAtUtc, Version = Version + 1 WHERE Id = @Id AND StatusKey = 'processing' AND Version = @Version AND LeaseExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkFailed = new("document.preview_task.mark_failed", """
        UPDATE fn_document_preview_task SET StatusKey = @StatusKey, ErrorCode = @ErrorCode, CompletedAtUtc = @CompletedAtUtc, NextAttemptAtUtc = @NextAttemptAtUtc, Version = Version + 1 WHERE Id = @Id AND StatusKey = 'processing' AND Version = @Version AND LeaseExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Cancel = new("document.preview_task.cancel", """
        UPDATE fn_document_preview_task SET StatusKey = 'cancelled', ErrorCode = 'document.preview.cancelled', CompletedAtUtc = @Now, Version = Version + 1 WHERE Id = @Id AND StatusKey IN ('preparing','pending','processing') AND Version = @Version
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement PreparationFailed = new("document.preview_task.preparation_failed", """
        UPDATE fn_document_preview_task SET StatusKey = 'failed', ErrorCode = @ErrorCode, CompletedAtUtc = @Now, Version = Version + 1 WHERE Id = @Id AND StatusKey = 'preparing'
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MaintenanceSqlServer = new("document.preview_task.maintenance.sqlserver", """
        SELECT TOP (100) Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc FROM fn_document_preview_task t WHERE StatusKey = 'preparing' OR (StatusKey IN ('failed','cancelled') AND EXISTS (SELECT 1 FROM fn_document_preview_reference r WHERE r.TaskId = t.Id AND r.Purpose = 'output' AND r.StateKey NOT IN ('release_pending','released'))) ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MaintenanceMySql = new("document.preview_task.maintenance.mysql", """
        SELECT Id, DocumentItemId, VersionId, DocumentTitle, SourceFileId, SourceFileName, SourceMimeType, OutputFileId, StatusKey, ProviderKey, ErrorCode, RequestedByUserId, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, Version, SourceReferenceId, OutputReferenceId, OutputOwnerId, AttemptCount, NextAttemptAtUtc, LeaseExpiresAtUtc FROM fn_document_preview_task t WHERE StatusKey = 'preparing' OR (StatusKey IN ('failed','cancelled') AND EXISTS (SELECT 1 FROM fn_document_preview_reference r WHERE r.TaskId = t.Id AND r.Purpose = 'output' AND r.StateKey NOT IN ('release_pending','released'))) ORDER BY CreatedAtUtc, Id LIMIT 100
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertAttempt = new("document.preview_task.insert_attempt", """
        INSERT INTO fn_document_preview_attempt (Id, TaskId, ClaimVersion, StartedAtUtc) VALUES (@AttemptId, @Id, @Version, @Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FinishAttempt = new("document.preview_task.finish_attempt", """
        UPDATE fn_document_preview_attempt SET CompletedAtUtc = @Now, ErrorCode = @ErrorCode WHERE TaskId = @Id AND ClaimVersion = @Version AND CompletedAtUtc IS NULL
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ExpirePreviousAttempts = new("document.preview_task.expire_previous_attempts", """
        UPDATE fn_document_preview_attempt SET CompletedAtUtc = @Now, ErrorCode = 'document.preview.lease_expired'
        WHERE TaskId = @Id AND ClaimVersion < @Version AND CompletedAtUtc IS NULL
        """, SqlDataScope.HostOnly);
}
