namespace Full.NET.Data.Dapper.Outbox;

internal sealed record OutboxMessage(
    Guid Id,
    string MessageType,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    DateTimeOffset OccurredAtUtc);
