namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 在回退准备 generation 已持久化后读取数据库 producer fence 位点。
/// 实现必须在活跃回退准备存在且 generation 匹配时才返回值。
/// </summary>
public interface IEventDeliveryProducerFencePositionReader
{
    /// <summary>
    /// 读取指定事件流的数据库 producer fence 位点快照。
    /// </summary>
    /// <param name="eventType">规范化事件类型（如 fullnet.organization.unit.changed）。</param>
    /// <param name="schemaVersion">结构版本正整数。</param>
    /// <param name="rollbackGeneration">回退准备代次；仅当活跃回退准备存在且代次匹配时才返回值。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>匹配的 fence 快照；无活跃回退准备或代次不匹配时为 <see langword="null"/>。</returns>
    Task<EventDeliveryProducerFenceSnapshot?> TryReadAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>数据库侧 producer fence 快照；不含 Broker/Connector 控制面状态。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RollbackGeneration">该 fence 对应的回退准备代次。</param>
/// <param name="ProducerFencePosition">fence 位点坐标；用于与 Connector 位点比较覆盖关系。</param>
/// <param name="LastPublishedEventId">fence 前最后发布的事件 ID；用于幂等表交叉校验，可空。</param>
/// <param name="ObservedAtUtc">快照观测时间（UTC）。</param>
public sealed record EventDeliveryProducerFenceSnapshot(
    Guid RollbackGeneration,
    CdcDeliveryPosition ProducerFencePosition,
    Guid? LastPublishedEventId,
    DateTimeOffset ObservedAtUtc);
