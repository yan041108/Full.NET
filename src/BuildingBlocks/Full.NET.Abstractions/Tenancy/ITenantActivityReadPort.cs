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

    /// <summary>读取活动状态并区分已确认不可恢复的关闭，供有待办记录的消费者选择终态。</summary>
    Task<TenantActivityState> ReadActivityAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>跨模块只读的租户活动分类，避免用非活动快照推断关闭。</summary>
public enum TenantActivityState
{
    /// <summary>当前不可用，但未取得已关闭的权威事实。</summary>
    Inactive = 0,
    /// <summary>当前可用。</summary>
    Active = 1,
    /// <summary>已正式关闭且不可恢复。</summary>
    Closed = 2
}
