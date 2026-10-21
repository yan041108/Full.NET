using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.TenantResourceFiles;

/// <summary>Host 范围汇总租户 ready 资源文件字节数，供 Tenancy 配额用量基线对账。</summary>
internal sealed class TenantResourceFileStorageUsagePort(IQueryExecutor queryExecutor)
    : ITenantResourceFileStorageUsagePort
{
    public async Task<IReadOnlyDictionary<Guid, long>> SumReadyStorageBytesByTenantsAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        if (tenantIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(tenantIds));
        var ids = tenantIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, _ => 0L);
        if (ids.Length == 0) return result;
        var rows = await queryExecutor.QueryAsync<TenantStorageUsageRow>(TenantResourceFileSql.SumReadyBytesByTenantsForHost,
            new Dictionary<string, object?> { ["TenantIds"] = ids }, cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) result[row.TenantId] = row.UsageValue;
        return result;
    }

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

/// <summary>权威用量批量聚合的固定列序投影。</summary>
internal sealed record TenantStorageUsageRow(Guid TenantId, long UsageValue)
{
    // MySQL SUM(bigint) 返回 decimal，按属性数值转换，不能要求构造器物理类型完全相等。
    public TenantStorageUsageRow() : this(default, default) { }
}
