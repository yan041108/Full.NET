namespace Full.NET.Modules.Messaging.Auditing;

/// <summary>Messaging B0 域内审计载体；Id 与 TraceId 由写入器补全。</summary>
internal sealed record MessagingDomainAuditWrite(
    string ActionKey,
    Guid EntityId,
    Guid? TenantId,
    string Outcome,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string? DiffSummaryJson);
