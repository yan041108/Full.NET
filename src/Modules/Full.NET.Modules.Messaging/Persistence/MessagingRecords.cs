namespace Full.NET.Modules.Messaging.Persistence;

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

internal sealed record OutboxStreamCutoffRecord(
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc);

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

internal sealed class RollbackPreparationRecord
{
    public int RollbackState { get; init; }
    public Guid? RollbackGeneration { get; init; }
    public DateTimeOffset? RollbackPreparedAtUtc { get; init; }
}

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
