namespace Full.NET.Messaging.Abstractions;

/// <summary>服务端实际观测目标链路与有界影子数据；禁止执行任何业务 Handler。</summary>
public interface IEventDeliveryCutoverProbe
{
    /// <summary>读取当前 Connector、环境配置及全部订阅实例的资源摘要；未知返回 null。</summary>
    Task<string?> ReadTargetFingerprintAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default);

    /// <summary>完整扫描配置的影子 Topic 固定高水位，并与权威快照逐条比对；不完整返回 null。</summary>
    Task<string?> CompareShadowAsync(string eventType, int schemaVersion, IReadOnlyList<CutoverShadowEvent> expected,
        CancellationToken cancellationToken = default);
}

/// <summary>从模块权威 Outbox 读取的有限样本，不通过 HTTP 接受载荷。</summary>
public sealed record CutoverShadowEvent(Guid Id, string PartitionKey, byte[] Payload, DateTimeOffset OccurredAtUtc,
    string ContentType, Guid? TenantId, string? CorrelationId,
    Guid? CausationId, string? TraceParent, string Producer, Guid CaptureGeneration);

/// <summary>未装配真实目标链路时保持失败关闭。</summary>
public sealed class FailClosedEventDeliveryCutoverProbe : IEventDeliveryCutoverProbe
{
    /// <inheritdoc/>
    public Task<string?> ReadTargetFingerprintAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    /// <inheritdoc/>
    public Task<string?> CompareShadowAsync(string eventType, int schemaVersion, IReadOnlyList<CutoverShadowEvent> expected,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
