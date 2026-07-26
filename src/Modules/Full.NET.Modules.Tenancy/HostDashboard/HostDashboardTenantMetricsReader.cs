using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.HostDashboard;

/// <summary>使用 Tenancy 自有表为 Host 工作台提供启用租户数量。</summary>
internal sealed class HostDashboardTenantMetricsReader(IQueryExecutor queryExecutor)
    : IHostDashboardTenantMetricsReader
{
    public Task<long> CountActiveTenantsAsync(
        CancellationToken cancellationToken = default) =>
        queryExecutor.QuerySingleOrDefaultAsync<long>(
            TenantSql.CountActiveTenants,
            cancellationToken: cancellationToken);
}
