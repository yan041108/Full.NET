namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 跨模块只读端口：判断租户是否仍处于活动状态（未暂停/关闭）。
/// </summary>
public interface ITenantActivityReadPort
{
    /// <summary>
    /// 判断指定租户是否处于活动状态。
    /// </summary>
    /// <param name="tenantId">待检查的租户标识。</param>
    /// <param name="cancellationToken">用于取消查询的令牌。</param>
    /// <returns>异步结果；租户存在且未暂停/关闭时返回 <see langword="true"/>，不存在或已停用返回 <see langword="false"/>。</returns>
    Task<bool> IsActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
