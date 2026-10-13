using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Tenancy 租户 Logo 精确引用探测。</summary>
internal sealed class TenancyTenantLogoReferenceProbe(IQueryExecutor queryExecutor)
    : IHostImmutableFileBindingClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Tenancy;

    /// <summary>一次读取租户当前 Logo；仅规范的一次性代际可获得明确退休结论，历史旧键保留。</summary>
    public async Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeBindingsAsync(
        IReadOnlyList<HostFileBindingClaimProbeRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests); cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count > 1000) throw new ArgumentOutOfRangeException(nameof(requests));
        var results = requests.Select(r => r.ClaimId).Distinct().ToDictionary(id => id,
            _ => new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.Failed));
        if (results.Count != requests.Count || requests.Any(r => r.ClaimId == Guid.Empty || r.ConsumerReferenceId == Guid.Empty || r.FileId == Guid.Empty)) return results;
        var valid = requests.Where(r => HostFileReferenceClaimIdempotencyKeys.IsGenerationBindingKey(
            HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(r.ConsumerReferenceId, r.FileId), r.IdempotencyKey)).ToArray();
        if (valid.Length == 0) return results;
        try
        {
            var branding = (await queryExecutor.QueryAsync<TenantBrandingRecord>(TenantSql.FindTenantBrandingByIds,
                TenancySqlParameters.Create(("TenantIds", valid.Select(r => r.ConsumerReferenceId).Distinct().ToArray())), cancellationToken)
                .ConfigureAwait(false)).ToDictionary(p => p.TenantId);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var request in valid)
                results[request.ClaimId] = new(branding.TryGetValue(request.ConsumerReferenceId, out var current)
                    ? ActiveOutcome(request, current) : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // 权威读取失败时整批保留，不能保留已经计算的局部 NotFound 回答。
            foreach (var id in results.Keys.ToArray()) results[id] = new(HostFileReferenceClaimProbeOutcome.Failed);
        }
        return results;
    }

    private static HostFileReferenceClaimProbeOutcome ActiveOutcome(HostFileBindingClaimProbeRequest request, TenantBrandingRecord current)
    {
        if (current.LogoFileId is null) return string.IsNullOrWhiteSpace(current.LogoClaimKey) ? HostFileReferenceClaimProbeOutcome.NotFound : HostFileReferenceClaimProbeOutcome.Failed;
        if (current.LogoFileId == Guid.Empty) return HostFileReferenceClaimProbeOutcome.Failed;
        var legacy = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(request.ConsumerReferenceId, current.LogoFileId.Value);
        if (!string.IsNullOrWhiteSpace(current.LogoClaimKey) && !HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, current.LogoClaimKey)) return HostFileReferenceClaimProbeOutcome.Failed;
        if (current.LogoFileId != request.FileId) return HostFileReferenceClaimProbeOutcome.NotFound;
        // 同文件的历史或未知绑定元数据不能证明扫描代际已经永久退出。
        if (!HostFileReferenceClaimIdempotencyKeys.IsGenerationBindingKey(legacy, current.LogoClaimKey!)) return HostFileReferenceClaimProbeOutcome.Failed;
        return string.Equals(current.LogoClaimKey, request.IdempotencyKey, StringComparison.Ordinal) ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound;
    }

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
