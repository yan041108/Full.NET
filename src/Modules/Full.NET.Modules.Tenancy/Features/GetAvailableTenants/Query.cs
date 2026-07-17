using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

internal sealed record Query : IQuery<TenantContextSummary[]>;

internal sealed class Handler(ITenantResolver tenantResolver)
    : IQueryHandler<Query, TenantContextSummary[]>
{
    public async Task<Result<TenantContextSummary[]>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var tenants = await tenantResolver.GetAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result<TenantContextSummary[]>.Success(tenants
            .Select(tenant => new TenantContextSummary(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                tenant.Domain))
            .ToArray());
    }
}
