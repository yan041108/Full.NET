namespace Full.NET.Data.Dapper.Outbox;

internal sealed record AppendOnlyOutboxMessage(
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