namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 供其他模块校验租户机构单元是否存在的只读目录。
/// </summary>
public interface ITenantOrganizationUnitDirectory
{
    /// <summary>
    /// 查找指定租户下活动机构单元；不存在、跨租户或已禁用时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="tenantId">租户标识；用于跨租户边界校验。</param>
    /// <param name="unitId">机构单元标识。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>匹配的活动机构单元目录项；不满足条件时为 <see langword="null"/>。</returns>
    Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default);
}

/// <summary>租户机构单元目录项（跨模块只读投影）。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">机构单元稳定标识。</param>
/// <param name="Code">机构单元编码；用于业务规则匹配与展示。</param>
/// <param name="Name">机构单元展示名称。</param>
public sealed record TenantOrganizationUnitDirectoryEntry(
    Guid Id,
    string Code,
    string Name);
