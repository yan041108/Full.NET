using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>只扫描固定近期窗口，ACL 在分页前执行，不读取内容、标签或全量计数。</summary>
internal static class DocumentSearchSql
{
    private const string Filter = $$"""
        FROM fn_document_item i
        WHERE i.TenantId IS NULL AND i.IsDeleted=0 AND i.IsPurging=0
          AND i.CreatedAtUtc>=@SinceUtc AND i.CreatedAtUtc<@UntilUtc
          AND {{DocumentPermissionSql.ReadPredicate}}
        """;
    internal static readonly SqlStatement SqlServer = new("document.host_search.sql_server", $$"""
        SELECT i.Id,i.Title,i.DocumentNo AS Number
        {{Filter}}
          AND (i.Title COLLATE Latin1_General_100_BIN2 LIKE @Pattern ESCAPE '~'
            OR i.DocumentNo COLLATE Latin1_General_100_BIN2 LIKE @Pattern ESCAPE '~')
        ORDER BY i.CreatedAtUtc DESC,i.Id DESC
        OFFSET @Offset ROWS FETCH NEXT 6 ROWS ONLY
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MySql = new("document.host_search.my_sql", $$"""
        SELECT i.Id,i.Title,i.DocumentNo AS Number
        {{Filter}}
          AND (i.Title COLLATE utf8mb4_bin LIKE @Pattern ESCAPE '~'
            OR i.DocumentNo COLLATE utf8mb4_bin LIKE @Pattern ESCAPE '~')
        ORDER BY i.CreatedAtUtc DESC,i.Id DESC
        LIMIT 6 OFFSET @Offset
        """, SqlDataScope.HostOnly);
}
