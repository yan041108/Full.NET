using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

internal static class MessagingDomainAuditSql
{
    public static readonly SqlStatement Insert = new(
        "messaging.domain_audit.insert",
        """
        INSERT INTO fn_messaging_domain_audit
            (Id, TenantId, ActionKey, EntityId, Outcome, ActorUserId, ActorDisplayName,
             TraceId, DiffSummaryJson, OccurredAtUtc)
        VALUES
            (@Id, @TenantId, @ActionKey, @EntityId, @Outcome, @ActorUserId, @ActorDisplayName,
             @TraceId, @DiffSummaryJson, @OccurredAtUtc)
        """,
        SqlDataScope.Global);
}