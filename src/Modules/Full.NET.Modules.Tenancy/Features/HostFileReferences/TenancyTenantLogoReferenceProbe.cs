using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Tenancy 租户 Logo 精确引用探测。</summary>
internal sealed class TenancyTenantLogoReferenceProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Tenancy;

    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    TenantSql.TenantLogoExists,
                    TenancySqlParameters.Create(
                        ("TenantId", consumerReferenceId),
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
