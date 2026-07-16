using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Tenancy.Contracts;

public sealed record ProvisionTenantRequest(
    string Identifier,
    string Name,
    string Domain);

public interface ITenantProvisioningService
{
    Task<Result<TenantSummary>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);
}
