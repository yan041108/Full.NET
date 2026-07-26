namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为 Identity 用例提供租户机构单元校验，避免 Identity 实现反向依赖 Organization 契约。
/// </summary>
public interface IIdentityOrganizationUnitDirectory
{
    /// <summary>
    /// 查找指定租户下的活动机构单元；不存在、跨租户或已停用时返回 <see langword="null"/>。
    /// </summary>
    Task<IdentityOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default);
}

/// <summary>Identity 所需的最小机构单元只读投影。</summary>
public sealed record IdentityOrganizationUnitDirectoryEntry(
    Guid Id,
    string Code,
    string Name);
