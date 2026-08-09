namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 事件流正在执行持久化回退准备，生产者必须在写入任何一种 Outbox 前失败。
/// 该 fence 会一直保留到同一 generation 完成回退或显式撤销。
/// </summary>
public sealed class EventDeliveryProducerFencedException(
    string eventType,
    int schemaVersion)
    : InvalidOperationException(
        $"Event stream ('{eventType}', schema {schemaVersion}) is fenced for delivery rollback.")
{
    public string EventType { get; } = eventType;

    public int SchemaVersion { get; } = schemaVersion;
}
