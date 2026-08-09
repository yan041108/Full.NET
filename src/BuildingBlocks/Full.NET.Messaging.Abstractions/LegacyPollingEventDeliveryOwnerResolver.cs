namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 未装配 Messaging 所有权目录时的兼容解析器；事件只能进入旧 Outbox 轮询路径。
/// </summary>
public sealed class LegacyPollingEventDeliveryOwnerResolver : IEffectiveEventDeliveryOwnerResolver
{
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
