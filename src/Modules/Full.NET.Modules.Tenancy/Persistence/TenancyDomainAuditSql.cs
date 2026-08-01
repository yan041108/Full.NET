using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// <c>fn_tenancy_domain_audit</c> 表的 SQL 语句；当前只服务 Host 上下文写入。
/// </summary>
internal static class TenancyDomainAuditSql
{
    public static readonly SqlStatement Insert = new(
        "tenancy.domain_audit.insert",
        """
        INSERT INTO fn_tenancy_domain_audit
            (Id, TenantId, ActionKey, EntityId, Outcome, ActorUserId, ActorDisplayName,
             TraceId, DiffSummaryJson, OccurredAtUtc)
        VALUES
            (@Id, @TenantId, @ActionKey, @EntityId, @Outcome, @ActorUserId, @ActorDisplayName,
             @TraceId, @DiffSummaryJson, @OccurredAtUtc)
        """,
        SqlDataScope.HostOnly);
}
