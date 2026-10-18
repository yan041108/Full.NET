using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Document.Features.HostFileReferences;

/// <summary>文档版本 UUID 不复用；保留回收站版本事实，允许对已物理删除的历史 Active 引用补偿。</summary>
internal sealed class HostDocumentVersionReferenceProbe(IQueryExecutor queryExecutor)
    : IHostImmutableFileReferenceClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Document;

    /// <summary>一次读取本模块版本，回收站保留的行仍回答 Exists；未知或无效快照保留。</summary>
    public async Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeReferencesAsync(
        IReadOnlyList<HostFileReferenceClaimProbeRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > 1000) throw new ArgumentOutOfRangeException(nameof(requests));
        var results = requests.ToDictionary(r => r.ClaimId,
            _ => new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.Failed));
        var valid = requests.Where(r => r.ClaimId != Guid.Empty && r.ConsumerReferenceId != Guid.Empty && r.FileId != Guid.Empty).ToArray();
        if (valid.Length == 0) return results;
        try
        {
            var versions = await queryExecutor.QueryAsync<DocumentVersionRecord>(DocumentItemSql.FindVersionsForClaimProbe,
                DocumentSqlParameters.Create(("VersionIds", valid.Select(r => r.ConsumerReferenceId).Distinct().ToArray())), cancellationToken)
                .ConfigureAwait(false);
            var files = versions.ToDictionary(v => v.Id, v => v.FileId);
            foreach (var request in valid)
            {
                var found = files.TryGetValue(request.ConsumerReferenceId, out var file);
                results[request.ClaimId] = new(found && file == Guid.Empty ? HostFileReferenceClaimProbeOutcome.Failed
                    : found && file == request.FileId ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // 整批保留默认 Failed；不可把读取故障变成任何一条不存在结论。
        }
        return results;
    }

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
                    DocumentSqlParameters.Create(("VersionId", consumerReferenceId), ("FileId", fileId)),
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
