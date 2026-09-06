using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.IntentAttachments;

/// <summary>为 Files claim 对账提供 Notifications Intent 附件精确引用探测。</summary>
internal sealed class NotificationIntentAttachmentProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceClaimProbe
{
    /// <inheritdoc />
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Notifications;

    /// <inheritdoc />
    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await queryExecutor
                .QuerySingleOrDefaultAsync<int>(
                    NotificationIntentAttachmentSql.ExistsForProbe,
                    NotificationPlatformSqlParameters.Create(
                        ("IntentId", consumerReferenceId),
                        ("FileId", fileId)),
                    cancellationToken)
                .ConfigureAwait(false);
            return new HostFileReferenceClaimProbeResult(
                exists == 1
                    ? HostFileReferenceClaimProbeOutcome.Exists
                    : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new HostFileReferenceClaimProbeResult(
                HostFileReferenceClaimProbeOutcome.Failed);
        }
    }
}
