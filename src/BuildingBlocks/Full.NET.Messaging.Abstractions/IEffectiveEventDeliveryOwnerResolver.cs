namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 解析事件流的有效交付所有权：目录默认值叠加持久化切流记录。
/// </summary>
public interface IEffectiveEventDeliveryOwnerResolver
{
    Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);
}
