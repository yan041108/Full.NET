using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.GetCurrentTenant;

internal sealed class Handler(IQueryExecutor queryExecutor)
    : IQueryHandler<GetCurrentTenantQuery, TenantSummary>
{
    public async Task<Result<TenantSummary>> HandleAsync(
        GetCurrentTenantQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantSummary>(
                TenantSql.GetCurrent,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? Result<TenantSummary>.Failure(new Error(
                "tenancy.not-found",
                "The current tenant was not found.",
                ErrorType.NotFound))
            : Result<TenantSummary>.Success(tenant);
    }
}
