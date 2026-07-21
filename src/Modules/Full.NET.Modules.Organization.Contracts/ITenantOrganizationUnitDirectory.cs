namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 供其他模块校验租户机构单元是否存在的只读目录。
/// </summary>
public interface ITenantOrganizationUnitDirectory
{
    /// <summary>
    /// 查找指定租户下活动机构单元；不存在、跨租户或已禁用时返回 <see langword="null"/>。
    /// </summary>
    Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default);
}

/// <summary>租户机构单元目录项（跨模块只读投影）。</summary>
public sealed record TenantOrganizationUnitDirectoryEntry(
    Guid Id,
    string Code,
    string Name);
