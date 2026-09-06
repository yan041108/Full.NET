using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;

namespace Full.NET.Modules.Tenancy.Features.PrintingBridge;

/// <summary>为 Printing 模块提供租户档案绑定数据。</summary>
internal sealed class TenancyPrintingTenantProfileBindingSource(
    HostTenantQueryService tenantQueries) : IPrintingTenantProfileBindingSource
{
    public async Task<PrintingTenantProfileBinding?> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await tenantQueries.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null || !result.Value.IsActive)
        {
            return null;
        }

        var tenant = result.Value;
        return new PrintingTenantProfileBinding(
            tenant.Name,
            tenant.Identifier,
            tenant.Domain);
    }
}
