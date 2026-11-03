using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>基础查询只读本模块摘要；机构权限由可信契约投影追加，不返回正文或总数。</summary>
internal static class EnterpriseRequestSearchSql
{
    internal const string ScopeMarker = "/* trusted_data_scope */";
    private const string Filter = """
        FROM demo_enterprise_request_enterprise_request
        WHERE TenantId=@TenantId AND IsDeleted=0
          AND CreatedAtUtc>=@SinceUtc AND CreatedAtUtc<@UntilUtc
          /* trusted_data_scope */
        """;
    internal static readonly SqlStatement SqlServer = new("enterprise_request.search.sql_server", $$"""
        SELECT Id,Title,RequestNumber AS Number
        {{Filter}}
          AND (Title COLLATE Latin1_General_100_BIN2 LIKE @Pattern ESCAPE '~'
            OR RequestNumber COLLATE Latin1_General_100_BIN2 LIKE @Pattern ESCAPE '~')
        ORDER BY CreatedAtUtc DESC,Id DESC
        OFFSET @Offset ROWS FETCH NEXT 6 ROWS ONLY
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MySql = new("enterprise_request.search.my_sql", $$"""
        SELECT Id,Title,RequestNumber AS Number
        {{Filter}}
          AND (Title COLLATE utf8mb4_bin LIKE @Pattern ESCAPE '~'
            OR RequestNumber COLLATE utf8mb4_bin LIKE @Pattern ESCAPE '~')
        ORDER BY CreatedAtUtc DESC,Id DESC
        LIMIT 6 OFFSET @Offset
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
