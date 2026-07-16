using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed class TenantProvisioningService(ICommandDispatcher dispatcher)
    : ITenantProvisioningService
{
    public Task<Result<TenantSummary>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return dispatcher.SendAsync<ProvisionTenantCommand, TenantSummary>(
            new ProvisionTenantCommand(
                request.Identifier,
                request.Name,
                request.Domain),
            cancellationToken);
    }
}
