using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.TenantResourceFiles;

/// <summary>Host 范围汇总租户 ready 资源文件字节数，供 Tenancy 配额用量基线对账。</summary>
internal sealed class TenantResourceFileStorageUsagePort(IQueryExecutor queryExecutor)
    : ITenantResourceFileStorageUsagePort
{
    public async Task<long> SumReadyStorageBytesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantResourceFileSql.SumReadyBytesByTenantForHost,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["TenantId"] = tenantId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return total;
    }
}
