using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;

/// <summary>Tenancy 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

internal sealed class TenantSubscriptionQueryService(IQueryExecutor queryExecutor, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task<Result<PagedResult<TenantSubscriptionResponse>>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(TenantSubscriptionSql.CountByTenant,
            parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql ? TenantSubscriptionSql.ListPageMySql : TenantSubscriptionSql.ListPageSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<TenantSubscriptionResponse>>.Success(new PagedResult<TenantSubscriptionResponse>(items, page, pageSize, total));
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
