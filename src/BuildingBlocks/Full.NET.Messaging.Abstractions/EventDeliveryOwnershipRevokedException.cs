namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Kafka 已取得消息，但该事件流不再由 CDC/Kafka 拥有时终止本次消费。
/// 此异常表示控制面状态变化，不属于业务失败，不应进入 Retry Topic 或 DLQ。
/// </summary>
public sealed class EventDeliveryOwnershipRevokedException(
    string eventType,
    int schemaVersion,
    EventDeliveryOwner actualOwner)
    : InvalidOperationException(
        $"CDC/Kafka delivery ownership is revoked for ('{eventType}', schema {schemaVersion}); "
        + $"current owner is '{actualOwner}'.")
{
    /// <summary>所有权被撤销的事件契约类型名。</summary>
    public string EventType { get; } = eventType;

    /// <summary>该事件契约的 Schema 版本号。</summary>
    public int SchemaVersion { get; } = schemaVersion;

    /// <summary>撤销后当前拥有该事件流投递权的所有者。</summary>
    public EventDeliveryOwner ActualOwner { get; } = actualOwner;
}
