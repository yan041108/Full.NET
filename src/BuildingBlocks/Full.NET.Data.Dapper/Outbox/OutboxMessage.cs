namespace Full.NET.Data.Dapper.Outbox;

internal sealed record OutboxMessage(
    Guid Id,
    string Type,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    DateTimeOffset OccurredAt);
