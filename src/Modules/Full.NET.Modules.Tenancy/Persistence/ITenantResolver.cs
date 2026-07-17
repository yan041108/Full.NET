using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Persistence;

internal interface ITenantResolver
{
    Task<TenantSummary?> ResolveByDomainAsync(
        string domain,
        CancellationToken cancellationToken = default);

    Task<TenantSummary?> ResolveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantSummary>> GetAvailableAsync(
        CancellationToken cancellationToken = default);
}
