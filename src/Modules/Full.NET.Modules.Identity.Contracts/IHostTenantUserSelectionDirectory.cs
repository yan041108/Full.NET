using Full.NET.Abstractions.Results;
using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 供 Host 侧已完成授权校验的业务模块读取指定租户内的活动用户目录。
/// </summary>
/// <remarks>
/// 调用方必须自行验证目标租户存在且调用者具备 Host 租户治理权限；
/// 实现只根据显式 <paramref name="tenantId"/> 投影 Identity 权威归属，不接受客户端自选租户上下文。
/// </remarks>
public interface IHostTenantUserSelectionDirectory
{
    /// <summary>分页读取指定租户内的活动成员用户。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">单页数量；实现必须施加受控上限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>租户成员分页结果。</returns>
    Task<PagedResult<HostTenantUserDirectoryEntry>> ListActiveTenantUsersAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>分页读取指定租户内的活动系统管理员账号。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">单页数量；实现必须施加受控上限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>租户管理员分页结果。</returns>
    Task<PagedResult<HostTenantUserDirectoryEntry>> ListActiveTenantAdministratorsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Host 侧查询租户用户目录时使用的最小跨模块只读投影。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">稳定用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="AccountType">账号类型机器码。</param>
/// <param name="IsActive">账号是否处于活动状态。</param>
/// <param name="PreferredLocale">账号已保存的规范语言偏好。</param>
public sealed record HostTenantUserDirectoryEntry(
    Guid Id,
    string Username,
    string DisplayName,
    string AccountType,
    bool IsActive,
    string PreferredLocale = LocaleCatalog.DefaultLocale);
