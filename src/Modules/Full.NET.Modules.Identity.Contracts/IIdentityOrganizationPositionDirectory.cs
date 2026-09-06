namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为 Identity 用例提供租户职位校验，避免 Identity 实现反向依赖 Organization 契约。
/// </summary>
public interface IIdentityOrganizationPositionDirectory
{
    /// <summary>
    /// 查找指定租户下的活动职位；不存在、跨租户或已停用时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="positionId">待查询的职位标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动职位投影；不存在、跨租户或已停用时返回 <see langword="null"/>。</returns>
    Task<IdentityOrganizationPositionDirectoryEntry?> FindActivePositionAsync(
        Guid tenantId,
        Guid positionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Identity 所需的最小职位只读投影。</summary>
/// <param name="Id">职位稳定标识。</param>
/// <param name="Code">职位编码。</param>
/// <param name="Name">职位名称。</param>
public sealed record IdentityOrganizationPositionDirectoryEntry(
    Guid Id,
    string Code,
    string Name);
