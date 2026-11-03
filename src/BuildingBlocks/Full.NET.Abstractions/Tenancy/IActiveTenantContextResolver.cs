namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 按稳定租户标识解析活动租户上下文，供跨模块用例建立受控租户作用域。
/// </summary>
public interface IActiveTenantContextResolver
{
    /// <summary>从权威源一次解析最多256个显式租户；缺失或禁用的租户不返回，不读取陈旧许可缓存。</summary>
    /// <param name="tenantIds">已由业务关系派生的非空租户Id集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>以租户Id索引的当前活动上下文。</returns>
    Task<IReadOnlyDictionary<Guid, TenantContext>> ResolveActiveByIdsAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按租户 Id 解析当前处于活动状态的租户上下文；已禁用或不存在的租户返回 null。
    /// </summary>
    /// <param name="tenantId">待解析的租户稳定唯一标识。</param>
    /// <param name="cancellationToken">用于取消解析查询的令牌。</param>
    /// <returns>活动租户的上下文；租户不存在或已被禁用时返回 <see langword="null"/>。</returns>
    Task<TenantContext?> ResolveActiveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
