using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为已完成授权校验的业务模块提供当前可信 Tenant 内活动成员候选目录。
/// </summary>
/// <remarks>
/// 成员关系以 <c>fn_identity_tenant_member</c> 为准，状态须为 <see cref="TenantMemberStatuses.Active"/>；
/// 调用方不得传入 TenantId 跨租户枚举。
/// </remarks>
public interface ITenantMemberSelectionDirectory
{
    /// <summary>分页读取当前 Tenant 的活动成员（含关联的活动 Host 用户资料）。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">单页数量；实现必须施加受控上限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前 Tenant 内活动成员的分页结果；越界页返回空列表但不抛异常。</returns>
    Task<PagedResult<TenantUserDirectoryEntry>> ListActiveTenantMembersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>查找当前 Tenant 内指定用户是否仍为活动成员。</summary>
    /// <param name="userId">待校验的稳定用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动成员条目；用户非活动、不存在或不属于当前 Tenant 时返回 null。</returns>
    Task<TenantUserDirectoryEntry?> FindActiveTenantMemberAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
