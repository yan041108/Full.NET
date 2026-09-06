using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档版本删除审计与保留裁剪相关 SQL。</summary>
internal static class DocumentVersionRetentionSql
{
    public static readonly SqlStatement CountVersionsByItemId = new(
        "document.host_version.count_by_item",
        """
        SELECT COUNT(1)
        FROM fn_document_version
        WHERE DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertDeletionAudit = new(
        "document.host_version.deletion_audit.insert",
        """
        INSERT INTO fn_document_version_deletion_audit
            (Id, DocumentItemId, VersionId, VersionNumber, FileId, ContentHash, SizeBytes,
             UploadedByUserId, VersionCreatedAtUtc, DeletedAtUtc, DeletedByUserId, DeletedBySourceKey)
        VALUES
            (@Id, @DocumentItemId, @VersionId, @VersionNumber, @FileId, @ContentHash, @SizeBytes,
             @UploadedByUserId, @VersionCreatedAtUtc, @DeletedAtUtc, @DeletedByUserId, @DeletedBySourceKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteVersionById = new(
        "document.host_version.delete_by_id",
        """
        DELETE FROM fn_document_version
        WHERE Id = @VersionId
          AND DocumentItemId = @DocumentItemId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListItemsWithExcessHistorySqlServer = new(
        "document.host_version.list_items_with_excess_history.sql_server",
        """
        SELECT TOP (@BatchSize)
            i.Id AS DocumentItemId,
            i.CurrentVersionId,
            COUNT(v.Id) AS HistoryCount
        FROM fn_document_item AS i
        INNER JOIN fn_document_version AS v
            ON v.DocumentItemId = i.Id
           AND v.Id <> i.CurrentVersionId
        WHERE i.TenantId IS NULL
          AND i.IsDeleted = 0
        GROUP BY i.Id, i.CurrentVersionId
        HAVING COUNT(v.Id) > @MaximumRetainedHistoryVersions
        ORDER BY MIN(v.VersionNumber), i.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListItemsWithExcessHistoryMySql = new(
        "document.host_version.list_items_with_excess_history.mysql",
        """
        SELECT
            grouped.DocumentItemId,
            grouped.CurrentVersionId,
            grouped.HistoryCount
        FROM (
            SELECT
                i.Id AS DocumentItemId,
                i.CurrentVersionId,
                COUNT(v.Id) AS HistoryCount,
                MIN(v.VersionNumber) AS OldestHistoryVersionNumber
            FROM fn_document_item AS i
            INNER JOIN fn_document_version AS v
                ON v.DocumentItemId = i.Id
               AND v.Id <> i.CurrentVersionId
            WHERE i.TenantId IS NULL
              AND i.IsDeleted = 0
            GROUP BY i.Id, i.CurrentVersionId
            HAVING COUNT(v.Id) > @MaximumRetainedHistoryVersions
        ) AS grouped
        ORDER BY grouped.OldestHistoryVersionNumber, grouped.DocumentItemId
        LIMIT @BatchSize
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOldestHistoryVersionsSqlServer = new(
        "document.host_version.list_oldest_history.sql_server",
        """
        SELECT TOP (@TakeCount)
            v.Id, v.DocumentItemId, v.FileId, v.VersionNumber, v.ContentHash, v.SizeBytes,
            v.ChangeDescription, v.UploadedByUserId, v.CreatedAtUtc
        FROM fn_document_version AS v
        WHERE v.DocumentItemId = @DocumentItemId
          AND v.Id <> @CurrentVersionId
        ORDER BY v.VersionNumber ASC, v.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOldestHistoryVersionsMySql = new(
        "document.host_version.list_oldest_history.mysql",
        """
        SELECT
            v.Id, v.DocumentItemId, v.FileId, v.VersionNumber, v.ContentHash, v.SizeBytes,
            v.ChangeDescription, v.UploadedByUserId, v.CreatedAtUtc
        FROM fn_document_version AS v
        WHERE v.DocumentItemId = @DocumentItemId
          AND v.Id <> @CurrentVersionId
        ORDER BY v.VersionNumber ASC, v.Id
        LIMIT @TakeCount
        """,
        SqlDataScope.HostOnly);
}

internal sealed class DocumentVersionRetentionCandidateRecord
{
    public Guid DocumentItemId { get; init; }

    public Guid CurrentVersionId { get; init; }

    public int HistoryCount { get; init; }
}
