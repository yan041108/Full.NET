namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为 Identity 用例提供活动租户存在性校验，避免 Identity 实现反向依赖 Tenancy 契约。
/// </summary>
public interface IIdentityActiveTenantDirectory
{
    /// <summary>
    /// 判断指定租户是否存在且处于活动状态。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>租户存在且活动时返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    Task<bool> IsActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
