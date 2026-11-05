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
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20, string? status = null)
    {
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (status is not null && status is not (TenantSubscriptionStatuses.Trial or TenantSubscriptionStatuses.Active
            or TenantSubscriptionStatuses.PastDue or TenantSubscriptionStatuses.Cancelled or TenantSubscriptionStatuses.Expired))
        {
            // 查询包含已结束的历史，不能借用仅允许创建非终态订阅的写入校验器。
            return Result<PagedResult<TenantSubscriptionResponse>>.Failure(new Error(TenancyErrorCodes.SubscriptionStatusInvalid,
                "Subscription status filter is invalid.", ErrorType.Validation));
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize), ("Status", status));
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
