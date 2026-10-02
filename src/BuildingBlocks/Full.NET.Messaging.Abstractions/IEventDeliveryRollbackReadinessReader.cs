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
    /// <param name="eventType">需要回退的事件类型标识。</param>
    /// <param name="schemaVersion">事件契约 Schema 版本；必须为正整数。</param>
    /// <param name="rollbackGeneration">本次回退的世代标识，用于关联 Prepare 与 Abort。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>控制面返回的回退就绪证明；未装配适配器时返回 <see cref="EventDeliveryRollbackReadiness.Unavailable"/>。</returns>
    Task<EventDeliveryRollbackReadiness> PrepareAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 最终所有权切换失败时撤销同一 generation 的控制面 fence。
    /// 实现必须幂等；撤销失败应保留停止状态并交由运维恢复，不能伪造成功。
    /// </summary>
    /// <param name="eventType">需要回退的事件类型标识。</param>
    /// <param name="schemaVersion">事件契约 Schema 版本；必须为正整数。</param>
    /// <param name="rollbackGeneration">与 PrepareAsync 相同的世代标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AbortAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>事件流回退时由外部控制面证明的安全边界。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RollbackGeneration">本次回退的世代标识，与 PrepareAsync/AbortAsync 调用参数一致。</param>
/// <param name="ConnectorStopped">Connector/Consumer 是否已停止且不再消费新消息。</param>
/// <param name="BrokerMessagesDrainedOrIsolated">Broker 中目标范围消息是否已被排空或隔离，确保回退后无新消息进入。</param>
/// <param name="SourcePositionCoversProducerFence">CDC 源位点是否已覆盖 Producer Fence 位置，保证回退后不会重复投递。</param>
/// <param name="ProducerFencePositionJson">Producer Fence 位置的 JSON 序列化形式；无 fence 时为 <see langword="null"/>。</param>
/// <param name="CdcSourcePositionJson">CDC 源位点的 JSON 序列化形式；无 CDC 时为 <see langword="null"/>。</param>
/// <param name="ControlPlaneFenceToken">控制面签发的 fence 令牌；用于验证所有权切换前的停止状态。</param>
/// <param name="LastPublishedEventId">切换前最后已发布事件标识；无已发布事件时为 <see langword="null"/>。</param>
/// <param name="ObservedAtUtc">控制面观测到该状态的时间（UTC）。</param>
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
    /// <summary>
    /// 未装配 Broker/Connector 控制面适配器时的失败关闭占位状态；
    /// 所有安全标志为 false，时间为 MinValue，调用方不得据此放行回退。
    /// </summary>
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
    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
