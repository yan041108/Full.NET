using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Document.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Document 版本精确引用探测。</summary>
internal sealed class HostDocumentVersionReferenceProbe(IQueryExecutor queryExecutor, IClock clock)
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
            var previews = await queryExecutor.QueryAsync<DocumentPreviewReferenceRecord>(DocumentPreviewReferenceSql.FindForClaimProbe,
                DocumentSqlParameters.Create(("ReferenceIds", valid.Select(r => r.ConsumerReferenceId).Distinct().ToArray()), ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
            var references = versions.Select(v => (v.Id, v.FileId)).Concat(previews.Select(p => (p.Id, p.FileId))).ToHashSet();
            foreach (var request in valid)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[request.ClaimId] = new(references.Contains((request.ConsumerReferenceId, request.FileId))
                    ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound);
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
            if (exists != 1) exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(DocumentPreviewReferenceSql.Exists,
                DocumentSqlParameters.Create(("ReferenceId", consumerReferenceId), ("FileId", fileId), ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
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
