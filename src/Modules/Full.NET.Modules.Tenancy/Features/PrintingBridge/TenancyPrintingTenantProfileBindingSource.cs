using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.PrintingBridge;

/// <summary>只为可信当前租户读取活动档案，不复用 Host 管理查询或跨模块表访问。</summary>
internal sealed class TenancyPrintingTenantProfileBindingSource(
    ICurrentTenant currentTenant, IQueryExecutor queries) : IPrintingTenantProfileBindingSource
{
    /// <inheritdoc />
    public async Task<PrintingTenantProfileBinding?> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (currentTenant.IsHost || currentTenant.Id != tenantId || tenantId == Guid.Empty) return null;
        var tenant = await queries.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            TenantSql.FindCurrentPrintingProfile, cancellationToken: cancellationToken).ConfigureAwait(false);
        return tenant is null ? null : new(tenant.Name, tenant.Identifier, tenant.Domain);
    }
}
