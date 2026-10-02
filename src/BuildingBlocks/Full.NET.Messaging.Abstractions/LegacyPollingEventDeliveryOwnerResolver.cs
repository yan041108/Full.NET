namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 未装配 Messaging 所有权目录时的兼容解析器；事件只能进入旧 Outbox 轮询路径。
/// </summary>
public sealed class LegacyPollingEventDeliveryOwnerResolver : IEffectiveEventDeliveryOwnerResolver
{
    /// <summary>始终返回 LegacyPolling 投递所有权，使事件进入旧 Outbox 轮询路径；仅用于未装配 Messaging 所有权目录的兼容场景。</summary>
    /// <param name="eventType">事件类型名；仅作非空校验，不参与所有权决策。</param>
    /// <param name="schemaVersion">事件契约版本；仅作正数校验，不参与所有权决策。</param>
    /// <param name="cancellationToken">预留取消令牌；解析器同步完成，不会触发取消。</param>
    /// <returns>固定为 <see cref="EventDeliveryOwner.LegacyPolling"/>。</returns>
    public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        return Task.FromResult(EventDeliveryOwner.LegacyPolling);
    }
}
