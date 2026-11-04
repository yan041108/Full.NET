using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>明细与主表均归属本模块，两库共用参数化 SQL 和可信租户绑定。</summary>
internal static class EnterpriseRequestLineSql
{
    internal static readonly SqlStatement Sum = new("enterprise_request.sum_lines", """
        SELECT SUM(LineAmount) FROM demo_enterprise_request_enterprise_request_line
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND IsDeleted = 0
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
