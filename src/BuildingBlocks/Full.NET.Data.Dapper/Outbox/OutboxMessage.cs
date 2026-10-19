namespace Full.NET.Data.Dapper.Outbox;

/// <summary>Worker 领取后的 Outbox 行投影；比写入信封少分区与追踪字段。</summary>
internal sealed record OutboxMessage(
    Guid Id,
    string MessageType,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string? TraceId,
    byte[] Payload,
    DateTimeOffset OccurredAtUtc);
