namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 在回退准备 generation 已持久化后读取数据库 producer fence 位点。
/// 实现必须在活跃回退准备存在且 generation 匹配时才返回值。
/// </summary>
public interface IEventDeliveryProducerFencePositionReader
{
    Task<EventDeliveryProducerFenceSnapshot?> TryReadAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>数据库侧 producer fence 快照；不含 Broker/Connector 控制面状态。</summary>
public sealed record EventDeliveryProducerFenceSnapshot(
    Guid RollbackGeneration,
    CdcDeliveryPosition ProducerFencePosition,
    Guid? LastPublishedEventId,
    DateTimeOffset ObservedAtUtc);
