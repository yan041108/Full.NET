namespace Full.NET.Data.Dapper.Outbox;

/// <summary>事务内写入 Outbox 的不可变信封；字段与 fn_outbox_message 列一一对应。</summary>
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