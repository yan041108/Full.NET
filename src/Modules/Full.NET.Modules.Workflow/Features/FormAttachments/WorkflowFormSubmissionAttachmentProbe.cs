using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>为 Files claim 对账提供 Workflow 表单提交附件精确引用探测。</summary>
internal sealed class WorkflowFormSubmissionAttachmentProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceClaimProbe
{
    /// <inheritdoc />
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Workflow;

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
                    WorkflowFormSubmissionAttachmentSql.ExistsForProbe,
                    WorkflowSqlParameters.Create(
                        ("SubmissionId", consumerReferenceId),
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
