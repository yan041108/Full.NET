namespace Full.NET.Data.Abstractions;

public sealed record OutboxEnvelope(
    Guid Id,
    Guid LockId,
    string MessageType,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    int Attempts,
    DateTimeOffset OccurredAtUtc);
