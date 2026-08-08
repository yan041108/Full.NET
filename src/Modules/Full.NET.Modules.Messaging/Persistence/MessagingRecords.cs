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
