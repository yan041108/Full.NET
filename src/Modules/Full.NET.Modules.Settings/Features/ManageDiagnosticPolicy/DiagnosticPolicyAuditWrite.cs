namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

internal static class DiagnosticPolicyAuditActionKeys
{
    public const string Updated = "settings.logging-diagnostic-policy.updated";
}

internal static class DiagnosticPolicyAuditOutcomes
{
    public const string Success = "success";
    public const string Failure = "failure";
}

/// <summary>Settings B0 域内审计写入载荷；必须与业务事务同提交。</summary>
internal sealed record DiagnosticPolicyAuditWrite(
    string ActionKey,
    Guid EntityId,
    Guid? TenantId,
    string Outcome,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string? DiffSummaryJson);
