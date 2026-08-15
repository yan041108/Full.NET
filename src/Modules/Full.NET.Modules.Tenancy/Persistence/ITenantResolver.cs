using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 租户解析服务抽象。解析链顺序（TenantResolutionMiddleware 协调）：
/// 1) 已认证且存在 TenantId Claim → 优先按 Id 解析（切换后写入的 Claim 优先）；
/// 2) 未认证但 Host 非白名单 → 按域名解析；
/// 3) HostDomain 命中 → 进入 Host Scope（不解析任何租户）。
/// 所有解析结果均通过 HybridCache（L1 内存 + L2 Redis + 标签）做两级缓存，
/// 失效由 TenantCacheInvalidator 在开通/变更/禁用提交后触发。
/// </summary>
internal interface ITenantResolver
{
    /// <summary>
    /// 按绑定域名解析租户；用于未登录或非 Host 域入口。
    /// </summary>
    /// <param name="domain">已小写并去尾点的规范域名。</param>
    Task<TenantSummary?> ResolveByDomainAsync(
        string domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按租户 ID 解析租户；用于已认证请求 TenantId Claim 路径与切换上下文校验。
    /// </summary>
    Task<TenantSummary?> ResolveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前全部活动租户列表；用于宿主管理端切换入口与可用租户查询。
    /// </summary>
    Task<IReadOnlyList<TenantSummary>> GetAvailableAsync(
        CancellationToken cancellationToken = default);
}
