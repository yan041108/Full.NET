using System.Diagnostics;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// Settings B0 审计写入器：把诊断策略变更写入 <c>fn_settings_domain_audit</c>。
/// 必须在调用方 <c>ICommandTransaction</c> 内执行，不自行开事务，不写 Outbox。
/// </summary>
internal sealed class DiagnosticPolicyAuditWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock) : ITransactionalDomainAuditWriter<DiagnosticPolicyAuditWrite>
{
    public async Task WriteAsync(
        DiagnosticPolicyAuditWrite auditWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditWrite);
        var traceId = Activity.Current?.TraceId.ToString();
        var affectedRows = await commandExecutor.ExecuteAsync(
                DiagnosticPolicyAuditSql.Insert,
                SettingsSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("TenantId", auditWrite.TenantId),
                    ("ActionKey", auditWrite.ActionKey),
                    ("EntityId", auditWrite.EntityId),
                    ("Outcome", auditWrite.Outcome),
                    ("ActorUserId", auditWrite.ActorUserId),
                    ("ActorDisplayName", auditWrite.ActorDisplayName),
                    ("TraceId", traceId),
                    ("DiffSummaryJson", auditWrite.DiffSummaryJson),
                    ("OccurredAtUtc", clock.UtcNow)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Settings domain audit insert affected {affectedRows} rows instead of 1.");
        }
    }
}
