using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档访问日志相关 SQL。</summary>
internal static class DocumentAccessLogSql
{
    public static readonly SqlStatement Insert = new(
        "document.host_access_log.insert",
        """
        INSERT INTO fn_document_access_log
            (Id, DocumentItemId, DocumentTitle, AccessTypeKey, SourceKey,
             ActorUserId, OccurredAtUtc, ClientIpFingerprint)
        VALUES
            (@Id, @DocumentItemId, @DocumentTitle, @AccessTypeKey, @SourceKey,
             @ActorUserId, @OccurredAtUtc, @ClientIpFingerprint)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement TouchItemAccess = new(
        "document.host_item.touch_access",
        """
        UPDATE fn_document_item
        SET LastAccessTime = @OccurredAtUtc,
            AccessCount = AccessCount + 1
        WHERE Id = @DocumentItemId
          AND TenantId IS NULL
          AND IsDeleted = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageSqlServer = new(
        "document.host_access_log.page.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_document_access_log AS log
        INNER JOIN fn_document_item AS item ON item.Id = log.DocumentItemId
        WHERE item.TenantId IS NULL
          AND (@DocumentItemId IS NULL OR log.DocumentItemId = @DocumentItemId);

        SELECT
            log.Id, log.DocumentItemId, log.DocumentTitle, log.AccessTypeKey, log.SourceKey,
            log.ActorUserId, log.OccurredAtUtc, log.ClientIpFingerprint
        FROM fn_document_access_log AS log
        INNER JOIN fn_document_item AS item ON item.Id = log.DocumentItemId
        WHERE item.TenantId IS NULL
          AND (@DocumentItemId IS NULL OR log.DocumentItemId = @DocumentItemId)
        ORDER BY log.OccurredAtUtc DESC, log.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageMySql = new(
        "document.host_access_log.page.mysql",
        """
        SELECT COUNT(1)
        FROM fn_document_access_log AS log
        INNER JOIN fn_document_item AS item ON item.Id = log.DocumentItemId
        WHERE item.TenantId IS NULL
          AND (@DocumentItemId IS NULL OR log.DocumentItemId = @DocumentItemId);

        SELECT
            log.Id, log.DocumentItemId, log.DocumentTitle, log.AccessTypeKey, log.SourceKey,
            log.ActorUserId, log.OccurredAtUtc, log.ClientIpFingerprint
        FROM fn_document_access_log AS log
        INNER JOIN fn_document_item AS item ON item.Id = log.DocumentItemId
        WHERE item.TenantId IS NULL
          AND (@DocumentItemId IS NULL OR log.DocumentItemId = @DocumentItemId)
        ORDER BY log.OccurredAtUtc DESC, log.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
