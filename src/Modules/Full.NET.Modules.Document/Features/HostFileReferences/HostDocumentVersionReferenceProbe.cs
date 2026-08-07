using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Document.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Document 版本精确引用探测。</summary>
internal sealed class HostDocumentVersionReferenceProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Document;

    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await queryExecutor
                .QuerySingleOrDefaultAsync<int>(
                    DocumentItemSql.VersionExistsByIdAndFile,
                    new { VersionId = consumerReferenceId, FileId = fileId },
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