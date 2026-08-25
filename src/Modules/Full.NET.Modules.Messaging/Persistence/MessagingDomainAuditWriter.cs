using System.Diagnostics;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Messaging.Auditing;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// 将 Messaging 域内审计与业务状态在同一事务写入；属于 B0 域内审计，fail-closed 且不使用 Outbox。
/// </summary>
/// <remarks>
/// 审计写入必须与业务写入共享同一命令事务，保证审计行与业务事实原子提交；
/// Id 与 OccurredAtUtc 由写入器补全，TraceId 取自当前活动，避免调用方暴露基础设施细节。
/// </remarks>
internal sealed class MessagingDomainAuditWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
{
    /// <summary>
    /// 在当前命令事务内写入一条域内审计；影响行数不为 1 时抛出异常以 fail-closed。
    /// </summary>
    public async Task WriteAsync(
        MessagingDomainAuditWrite auditWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditWrite);
        var traceId = Activity.Current?.TraceId.ToString();
        IReadOnlyDictionary<string, object?> parameters =
            new Dictionary<string, object?>
            {
                ["Id"] = idGenerator.NewId(),
                ["TenantId"] = auditWrite.TenantId,
                ["ActionKey"] = auditWrite.ActionKey,
                ["EntityId"] = auditWrite.EntityId,
                ["Outcome"] = auditWrite.Outcome,
                ["ActorUserId"] = auditWrite.ActorUserId,
                ["ActorDisplayName"] = auditWrite.ActorDisplayName,
                ["TraceId"] = traceId,
                ["DiffSummaryJson"] = auditWrite.DiffSummaryJson,
                ["OccurredAtUtc"] = clock.UtcNow,
            };
        var affectedRows = await commandExecutor.ExecuteAsync(
                MessagingDomainAuditSql.Insert,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Messaging domain audit insert affected {affectedRows} rows instead of 1.");
        }
    }
}
