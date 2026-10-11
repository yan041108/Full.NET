using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>明细与主表均归属本模块，两库共用参数化 SQL 和可信租户绑定。</summary>
internal static class EnterpriseRequestLineSql
{
    internal static readonly SqlStatement Read = new("enterprise_request.read_lines", """
        SELECT Id, TenantId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount
        FROM demo_enterprise_request_enterprise_request_line
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND IsDeleted = 0
        ORDER BY LineNumber, Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Sum = new("enterprise_request.sum_lines", """
        SELECT SUM(LineAmount) FROM demo_enterprise_request_enterprise_request_line
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND IsDeleted = 0
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateParent = new("enterprise_request.replace_lines_parent", """
        UPDATE demo_enterprise_request_enterprise_request
        SET TotalAmount = @TotalAmount, Version = Version + 1, UpdatedAtUtc = @Now, UpdatedById = @ActorId
        WHERE TenantId = @TenantId AND Id = @RequestId AND OrganizationUnitId = @OrganizationUnitId
          AND IsDeleted = 0 AND Status = @ExpectedStatus AND Version = @Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Delete = new("enterprise_request.replace_lines_delete", """
        DELETE FROM demo_enterprise_request_enterprise_request_line
        WHERE TenantId = @TenantId AND RequestId = @RequestId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("enterprise_request.replace_lines_insert", """
        INSERT INTO demo_enterprise_request_enterprise_request_line
          (Id, TenantId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount,
           Version, CreatedAtUtc, CreatedById, IsDeleted)
        VALUES (@Id, @TenantId, @RequestId, @LineNumber, @ItemDescription, @Quantity, @UnitPrice, @LineAmount,
           1, @Now, @ActorId, 0)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>明细读取保留内部归属，返回前再次检查身份。</summary>
internal sealed record EnterpriseRequestLineRow(Guid Id, Guid TenantId, Guid RequestId, int LineNumber,
    string ItemDescription, decimal Quantity, decimal UnitPrice, decimal LineAmount);
