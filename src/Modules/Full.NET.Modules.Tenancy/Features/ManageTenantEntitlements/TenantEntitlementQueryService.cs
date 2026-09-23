using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

internal sealed class TenantEntitlementQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<TenantEntitlementCatalogResponse>>> ListCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.ListCatalog,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapCatalog).ToArray();
        return Result<IReadOnlyList<TenantEntitlementCatalogResponse>>.Success(items);
    }

    public async Task<Result<IReadOnlyList<TenantEntitlementBindingResponse>>> ListBindingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<TenantEntitlementBindingRecord>(
                TenantEntitlementSql.ListBindings,
                Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapBinding).ToArray();
        return Result<IReadOnlyList<TenantEntitlementBindingResponse>>.Success(items);
    }

    public async Task<Result<TenantEntitlementEnforcementResponse>> GetEnforcementPhaseAsync(
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(
                TenantEntitlementSql.GetEnforcementPhase,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("SettingsId", TenancySettingsConstants.SettingsId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<TenantEntitlementEnforcementResponse>.Failure(new Error(
                TenancyErrorCodes.SettingsNotFound,
                "Tenancy settings were not found.",
                ErrorType.NotFound));
        }

        return Result<TenantEntitlementEnforcementResponse>.Success(
            new TenantEntitlementEnforcementResponse(row.EntitlementEnforcementPhase, row.Version));
    }

    private static TenantEntitlementCatalogResponse MapCatalog(TenantEntitlementCatalogRecord row) =>
        new(row.Id, row.Code, row.Name, row.Description, row.EntitlementType, row.IsActive, row.Version);

    private static TenantEntitlementBindingResponse MapBinding(TenantEntitlementBindingRecord row) =>
        new(row.Id, row.TenantId, row.EntitlementId, row.EntitlementCode, row.EntitlementName,
            row.EffectiveFromUtc, row.EffectiveToUtc, row.SourcePackageId, row.Version);
}
