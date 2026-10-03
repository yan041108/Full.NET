using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>Files 只接受 AI 持久化的精确版本/文件关系，不把处理审批或用户下载权当引用证明。</summary>
internal sealed class AiKnowledgeFileReferenceProbe(IQueryExecutor query) : IHostFileReferenceClaimProbe, ITenantResourceFileOwner
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Ai;
    public string OwnerModuleKey => "ai";

    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(Guid consumerReferenceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        if (consumerReferenceId == Guid.Empty || fileId == Guid.Empty) return new(HostFileReferenceClaimProbeOutcome.NotFound);
        try
        {
            var exists = await query.QuerySingleOrDefaultAsync<long>(AiKnowledgeImportSql.ReferenceHost,
                new Dictionary<string, object?> { ["VersionId"] = consumerReferenceId, ["FileId"] = fileId }, cancellationToken).ConfigureAwait(false);
            return new(exists > 0 ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(HostFileReferenceClaimProbeOutcome.Failed); }
    }

    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        if (resourceId == Guid.Empty || fileId == Guid.Empty) return false;
        return await query.QuerySingleOrDefaultAsync<long>(AiKnowledgeImportSql.ReferenceTenant,
            new Dictionary<string, object?> { ["VersionId"] = resourceId, ["FileId"] = fileId }, cancellationToken).ConfigureAwait(false) > 0;
    }
}
