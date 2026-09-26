using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

internal sealed class TenantEntitlementBackfillService(
    IQueryExecutor queryExecutor,
    IClock clock,
    TenantEntitlementManagementService management,
    IOptions<TenancyCommercialOptions> commercialOptions)
{
    public async Task<Result<TenantEntitlementBackfillResponse>> ReconcileAsync(
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var tenants = await queryExecutor.QueryAsync<TenantEntitlementBackfillCandidate>(
                TenantEntitlementSql.ListTenantsWithoutActiveBinding,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("LifecycleStatus", TenantLifecycleStatuses.Active),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        var candidates = tenants.ToList();
        if (dryRun || candidates.Count == 0)
        {
            return Result<TenantEntitlementBackfillResponse>.Success(
                new TenantEntitlementBackfillResponse(
                    candidates.Count,
                    0,
                    dryRun,
                    candidates.Select(row => row.TenantId).ToList()));
        }

        var catalog = await ResolveCompatibilityCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (catalog is null)
        {
            return Result<TenantEntitlementBackfillResponse>.Failure(new Error(
                TenancyErrorCodes.EntitlementBindingNotFound,
                "No compatibility entitlement catalog entry is configured for backfill.",
                ErrorType.NotFound));
        }

        var applied = 0;
        var now = clock.UtcNow;
        foreach (var candidate in candidates)
        {
            var binding = await management.CreateBindingAsync(
                    candidate.TenantId,
                    new CreateTenantEntitlementBindingRequest(
                        catalog.Id,
                        now,
                        null,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (binding.IsSuccess)
            {
                applied += 1;
            }
        }

        return Result<TenantEntitlementBackfillResponse>.Success(
            new TenantEntitlementBackfillResponse(
                candidates.Count,
                applied,
                false,
                candidates.Select(row => row.TenantId).ToList()));
    }

    private async Task<TenantEntitlementCatalogRecord?> ResolveCompatibilityCatalogAsync(
        CancellationToken cancellationToken)
    {
        var configuredCode = commercialOptions.Value.CompatibilityBackfillEntitlementCode?.Trim();
        if (!string.IsNullOrEmpty(configuredCode))
        {
            return await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                    TenantEntitlementSql.FindCatalogByCode,
                    Tenancy.Persistence.TenancySqlParameters.Create(("Code", configuredCode)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var catalog = await queryExecutor.QueryAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.ListCatalog,
                Tenancy.Persistence.TenancySqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        return catalog.FirstOrDefault(entry =>
            entry.IsActive
            && string.Equals(
                entry.Code,
                TenantEntitlementCatalogCodes.CompatibilityBaseline,
                StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record TenantEntitlementBackfillCandidate(Guid TenantId);
