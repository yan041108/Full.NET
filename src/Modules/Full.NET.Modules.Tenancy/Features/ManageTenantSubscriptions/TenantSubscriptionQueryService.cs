using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

internal sealed class TenantSubscriptionQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<TenantSubscriptionResponse>>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                TenantSubscriptionSql.ListByTenant,
                Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<TenantSubscriptionResponse>>.Success(items);
    }

    private static TenantSubscriptionResponse Map(TenantSubscriptionRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.PackageId,
            row.Status,
            row.TrialEndsAtUtc,
            row.CurrentPeriodStartUtc,
            row.CurrentPeriodEndUtc,
            row.CancelledAtUtc,
            row.Version);
}
