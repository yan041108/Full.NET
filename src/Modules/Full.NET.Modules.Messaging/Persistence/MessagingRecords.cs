namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>消费死信表行的 Dapper 投影模型，列名与 PascalCase 列直接映射。</summary>
internal sealed record DeadLetterRecord(
    string ConsumerName,
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    Guid? TenantId,
    int Attempts,
    DateTimeOffset ReceivedAtUtc,
    string? LastErrorCode,
    string? LastError);

/// <summary>事件流切流边界事件投影，用于标记 Legacy 发布链路的最后一条事件。</summary>
internal sealed record OutboxStreamCutoffRecord(
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc);

/// <summary>
/// 事件流所有权表行的 Dapper 投影模型；所有者字段以 int 存储，由 Mapper 与 <see cref="EventDeliveryOwner"/> 枚举互转。
/// </summary>
internal sealed class EventStreamOwnershipPersistenceRow
{
    public string MessageType { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public string TopicCode { get; init; } = string.Empty;
    public int CurrentOwner { get; init; }
    public int PreviousOwner { get; init; }
    public Guid CutoffEventId { get; init; }
    public DateTimeOffset CutoffOccurredAtUtc { get; init; }
    public string? CdcSourcePositionJson { get; init; }
    public Guid? OperatorUserId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? RollbackBoundaryEventId { get; init; }
    public DateTimeOffset? RollbackOccurredAtUtc { get; init; }
    public int RollbackState { get; init; }
    public Guid? RollbackGeneration { get; init; }
    public DateTimeOffset? RollbackPreparedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>回退准备阶段的状态投影，用于校验回退代次与准备态。</summary>
internal sealed class RollbackPreparationRecord
{
    public int RollbackState { get; init; }
    public Guid? RollbackGeneration { get; init; }
    public DateTimeOffset? RollbackPreparedAtUtc { get; init; }
}

/// <summary>Outbox 事件信封投影，用于死信重放时重建 <see cref="IntegrationEventEnvelope"/>。</summary>
internal sealed record OutboxEnvelopeRecord(
    Guid Id,
    string MessageType,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string PartitionKey,
    string? CorrelationId,
    Guid? CausationId,
    string? TraceParent,
    string Producer,
    byte[] Payload,
    DateTimeOffset OccurredAtUtc);
