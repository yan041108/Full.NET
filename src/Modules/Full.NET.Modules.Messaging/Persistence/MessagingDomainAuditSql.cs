using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// Messaging B0 域内审计表的参数化 SQL 语句集合，声明为 <see cref="SqlDataScope.Global"/>。
/// </summary>
/// <remarks>
/// 审计与业务状态在同一事务原子写入并 fail-closed；Id 与 OccurredAtUtc 由写入器补全，不在调用方暴露。
/// </remarks>
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