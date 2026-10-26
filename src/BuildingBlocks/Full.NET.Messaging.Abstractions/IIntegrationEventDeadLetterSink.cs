using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Messaging.Abstractions;

/// <summary>接收从真实 Broker DLQ 读取的记录；返回前必须持久化，失败时不得确认来源 Offset。</summary>
public interface IIntegrationEventDeadLetterSink
{
    /// <summary>按 DLQ Topic、Partition、Offset 幂等保存失败消息。</summary>
    Task CaptureAsync(IntegrationEventDeadLetterCapture capture, CancellationToken cancellationToken);
}

/// <summary>持久化 DLQ 元数据及受控信封；不向业务模块泄漏 Kafka SDK 类型。</summary>
public sealed record IntegrationEventDeadLetterCapture(Guid Id, string ConsumerName, string DlqTopic,
    int DlqPartition, long DlqOffset, string SourceTopic, int SourcePartition, long SourceOffset,
    Guid? MessageId, string? MessageType, int? SchemaVersion, Guid? TenantId, int Attempts,
    string ErrorCode, DateTimeOffset FirstFailedAtUtc, DateTimeOffset LastFailedAtUtc,
    bool CanReplay, string SnapshotJson);

/// <summary>保留完整合法信封供重放；损坏记录只保留原始位置和有界原始消息。</summary>
public sealed record DeadLetterMessageSnapshot(string? Key, byte[]? RawPayload,
    IReadOnlyList<DeadLetterMessageHeader> Headers, DeadLetterReplayEnvelope? Envelope)
{
    /// <summary>以静态 JSON 闭包保存快照，避免依赖生产 Outbox 保留期。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, DeadLetterSnapshotJsonContext.Default.DeadLetterMessageSnapshot);

    /// <summary>只按已登记类型读取持久快照。</summary>
    public static DeadLetterMessageSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize(json, DeadLetterSnapshotJsonContext.Default.DeadLetterMessageSnapshot)
        ?? throw new InvalidOperationException("messaging.dead_letter.snapshot_invalid");
}

/// <summary>白名单事件头的原始值，用于损坏消息定位；不收集任意外来 Header。</summary>
public sealed record DeadLetterMessageHeader(string Key, byte[]? Value);

/// <summary>重放时恢复的不可变业务信封，保留原消息标识和租户边界。</summary>
public sealed record DeadLetterReplayEnvelope(Guid EventId, string MessageType, int SchemaVersion,
    string ContentType, Guid? TenantId, string PartitionKey, string? CorrelationId, Guid? CausationId,
    string? TraceParent, string Producer, DateTimeOffset OccurredAtUtc, byte[] Payload)
{
    /// <summary>重建信封时重新执行契约校验。</summary>
    public IntegrationEventEnvelope ToEnvelope() => IntegrationEventEnvelope.Create(EventId, MessageType,
        SchemaVersion, ContentType, TenantId, PartitionKey, CorrelationId, CausationId, TraceParent,
        Producer, OccurredAtUtc, Payload);

    /// <summary>从已校验信封创建独立快照。</summary>
    public static DeadLetterReplayEnvelope FromEnvelope(IntegrationEventEnvelope value) => new(value.EventId,
        value.MessageType, value.SchemaVersion, value.ContentType, value.TenantId, value.PartitionKey,
        value.CorrelationId, value.CausationId, value.TraceParent, value.Producer, value.OccurredAtUtc,
        value.Payload.ToArray());
}

/// <summary>DLQ 快照源生成闭包，原始字节仅在持久化边界使用。</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeadLetterMessageSnapshot))]
internal partial class DeadLetterSnapshotJsonContext : JsonSerializerContext;
