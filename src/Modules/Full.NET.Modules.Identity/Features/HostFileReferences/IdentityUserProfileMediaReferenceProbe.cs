using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Identity 用户资料媒体精确引用探测。</summary>
internal sealed class IdentityUserProfileMediaReferenceProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Identity;

    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    IdentitySql.HostUserProfileMediaExists,
                    IdentitySqlParameters.Create(
                        ("UserId", consumerReferenceId),
                        ("FileId", fileId),
                        ("Kind", "avatar")),
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists == 1)
            {
                return new HostFileReferenceClaimProbeResult(
                    HostFileReferenceClaimProbeOutcome.Exists);
            }

            exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    IdentitySql.HostUserProfileMediaExists,
                    IdentitySqlParameters.Create(
                        ("UserId", consumerReferenceId),
                        ("FileId", fileId),
                        ("Kind", "signature")),
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
