using System.Diagnostics;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Messaging.Auditing;

namespace Full.NET.Modules.Messaging.Persistence;

internal sealed class MessagingDomainAuditWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
{
    public async Task WriteAsync(
        MessagingDomainAuditWrite auditWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditWrite);
        var traceId = Activity.Current?.TraceId.ToString();
        var affectedRows = await commandExecutor.ExecuteAsync(
                MessagingDomainAuditSql.Insert,
                new
                {
                    Id = idGenerator.NewId(),
                    auditWrite.TenantId,
                    auditWrite.ActionKey,
                    auditWrite.EntityId,
                    auditWrite.Outcome,
                    auditWrite.ActorUserId,
                    auditWrite.ActorDisplayName,
                    TraceId = traceId,
                    auditWrite.DiffSummaryJson,
                    OccurredAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Messaging domain audit insert affected {affectedRows} rows instead of 1.");
        }
    }
}
