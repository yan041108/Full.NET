using Full.NET.Abstractions.Results;
using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为已完成授权校验的业务模块提供当前可信 Tenant 内的活动用户候选目录。</summary>
/// <remarks>实现必须从请求租户上下文确定作用域，调用方不得传入 TenantId 选择其他租户。</remarks>
public interface ITenantUserSelectionDirectory
{
    /// <summary>分页读取当前 Tenant 直属用户或拥有当前 Tenant 活动角色的 Host 活动用户。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">单页数量；实现必须施加受控上限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前 Tenant 的最小用户候选分页结果。</returns>
    Task<PagedResult<TenantUserDirectoryEntry>> ListActiveTenantUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>批量查找当前 Tenant 内仍处于活动状态的指定用户。</summary>
    /// <param name="userIds">待校验的稳定用户标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以用户标识为键的当前 Tenant 活动用户目录；无效、停用或跨租户用户不会进入结果。</returns>
    Task<IReadOnlyDictionary<Guid, TenantUserDirectoryEntry>> FindActiveTenantUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Tenant 用户候选的最小跨模块只读投影。</summary>
/// <param name="Id">稳定用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="PreferredLocale">账号已保存的规范语言偏好。</param>
public sealed record TenantUserDirectoryEntry(
    Guid Id,
    string Username,
    string DisplayName,
    string PreferredLocale = LocaleCatalog.DefaultLocale);
