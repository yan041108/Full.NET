using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Tenancy 租户 Logo 精确引用探测。</summary>
internal sealed class TenancyTenantLogoReferenceProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceBindingClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Tenancy;

    public async Task<HostFileReferenceClaimProbeResult> ProbeBindingAsync(Guid consumerReferenceId, Guid fileId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (consumerReferenceId == Guid.Empty || fileId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey))
            return new(HostFileReferenceClaimProbeOutcome.Failed);
        try
        {
            // Host 后台权威读取只匹配当前 Logo 的有效绑定键，另一代际的文件存在不能晋升扫描 Claim。
            var branding = await queryExecutor.QuerySingleOrDefaultAsync<TenantBrandingRecord>(TenantSql.FindTenantBrandingById,
                TenancySqlParameters.Create(("TenantId", consumerReferenceId)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (branding is null || branding.LogoFileId != fileId) return new(HostFileReferenceClaimProbeOutcome.NotFound);
            var legacy = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(consumerReferenceId, fileId);
            var effective = branding.LogoClaimKey is not null && HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, branding.LogoClaimKey)
                ? branding.LogoClaimKey : legacy;
            return new(string.Equals(effective, idempotencyKey, StringComparison.Ordinal)
                ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(HostFileReferenceClaimProbeOutcome.Failed); }
    }

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
