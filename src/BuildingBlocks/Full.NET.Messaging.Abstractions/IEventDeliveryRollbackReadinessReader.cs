namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 从 Broker/Connector 控制面读取回退前置状态。实现必须返回当前真实状态，
/// 不能把请求参数或人工确认直接当作已验证证据。
/// </summary>
public interface IEventDeliveryRollbackReadinessReader
{
    /// <summary>
    /// 在数据库事务外停止并栅栏 Connector/Consumer，返回在所有权切换提交前保持有效的证明。
    /// </summary>
    Task<EventDeliveryRollbackReadiness> PrepareAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 最终所有权切换失败时撤销同一 generation 的控制面 fence。
    /// 实现必须幂等；撤销失败应保留停止状态并交由运维恢复，不能伪造成功。
    /// </summary>
    Task AbortAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>事件流回退时由外部控制面证明的安全边界。</summary>
public sealed record EventDeliveryRollbackReadiness(
    Guid RollbackGeneration,
    bool ConnectorStopped,
    bool BrokerMessagesDrainedOrIsolated,
    bool SourcePositionCoversProducerFence,
    string? ProducerFencePositionJson,
    string? CdcSourcePositionJson,
    string? ControlPlaneFenceToken,
    Guid? LastPublishedEventId,
    DateTimeOffset ObservedAtUtc)
{
    public static EventDeliveryRollbackReadiness Unavailable { get; } =
        new(
            RollbackGeneration: Guid.Empty,
            ConnectorStopped: false,
            BrokerMessagesDrainedOrIsolated: false,
            SourcePositionCoversProducerFence: false,
            ProducerFencePositionJson: null,
            CdcSourcePositionJson: null,
            ControlPlaneFenceToken: null,
            LastPublishedEventId: null,
            ObservedAtUtc: DateTimeOffset.MinValue);
}

/// <summary>
/// 未装配 Broker/Connector 控制面适配器时失败关闭，禁止仅凭 API 调用切回旧 Worker。
/// </summary>
public sealed class FailClosedEventDeliveryRollbackReadinessReader
    : IEventDeliveryRollbackReadinessReader
{
    public Task<EventDeliveryRollbackReadiness> PrepareAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        return Task.FromResult(EventDeliveryRollbackReadiness.Unavailable);
    }

    public Task AbortAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        return Task.CompletedTask;
    }
}
