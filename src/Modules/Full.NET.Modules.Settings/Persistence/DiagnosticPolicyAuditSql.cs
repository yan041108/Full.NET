using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary><c>fn_settings_domain_audit</c> 插入语句；只服务 Host 上下文 B0 写入。</summary>
internal static class DiagnosticPolicyAuditSql
{
    public static readonly SqlStatement Insert = new(
        "settings.domain_audit.insert",
        """
        INSERT INTO fn_settings_domain_audit
            (Id, TenantId, ActionKey, EntityId, Outcome, ActorUserId, ActorDisplayName,
             TraceId, DiffSummaryJson, OccurredAtUtc)
        VALUES
            (@Id, @TenantId, @ActionKey, @EntityId, @Outcome, @ActorUserId, @ActorDisplayName,
             @TraceId, @DiffSummaryJson, @OccurredAtUtc)
        """,
        SqlDataScope.HostOnly);
}
