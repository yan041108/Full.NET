namespace Full.NET.Data.Abstractions;

public sealed record OutboxEnvelope(
    Guid Id,
    Guid LockId,
    string Type,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    int Attempts,
    DateTimeOffset OccurredAt);
