using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;

internal sealed class TenantCommercialReactivateGate(
    IOptions<TenancyCommercialOptions> commercialOptions,
    TenantEntitlementQueryService entitlementQueries,
    IQueryExecutor queryExecutor)
{
    private static readonly HashSet<string> ActiveSubscriptionStatuses = new(StringComparer.Ordinal)
    {
        TenantSubscriptionStatuses.Trial,
        TenantSubscriptionStatuses.Active,
        TenantSubscriptionStatuses.PastDue,
    };

    public async Task<Result<bool>> ValidateAsync(
        TenantSummary tenant,
        CancellationToken cancellationToken = default)
    {
        if (!commercialOptions.Value.RequirePackageOrSubscriptionOnReactivate)
        {
            return Result<bool>.Success(true);
        }

        var phaseResult = await entitlementQueries.GetEnforcementPhaseAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!phaseResult.IsSuccess)
        {
            return Result<bool>.Failure(phaseResult.Error!);
        }

        if (!string.Equals(
                phaseResult.Value!.Phase,
                TenantEntitlementEnforcementPhases.Enforced,
                StringComparison.Ordinal))
        {
            return Result<bool>.Success(true);
        }

        if (tenant.TenantPackageId is Guid)
        {
            return Result<bool>.Success(true);
        }

        var subscriptions = await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                TenantSubscriptionSql.ListByTenant,
                Persistence.TenancySqlParameters.Create(("TenantId", tenant.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        if (subscriptions.Any(row => ActiveSubscriptionStatuses.Contains(row.Status)))
        {
            return Result<bool>.Success(true);
        }

        return Result<bool>.Failure(new Error(
            TenancyErrorCodes.EntitlementCommercialBindingRequired,
            "An active tenant package or subscription is required before reactivation.",
            ErrorType.BusinessRule));
    }
}