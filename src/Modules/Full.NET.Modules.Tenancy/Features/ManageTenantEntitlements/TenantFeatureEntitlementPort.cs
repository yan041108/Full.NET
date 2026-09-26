using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

/// <summary>按执行阶段与绑定关系评估租户功能权益；Enforced 阶段缺绑定则拒绝。</summary>
internal sealed class TenantFeatureEntitlementPort(
    IQueryExecutor queryExecutor,
    TenantEntitlementQueryService entitlementQueries,
    ICurrentTenantContextWriter currentTenantWriter,
    IClock clock) : ITenantFeatureEntitlementPort
{
    public Task<Result<bool>> IsFeatureGrantedAsync(
        Guid tenantId,
        string entitlementCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = entitlementCode?.Trim() ?? string.Empty;
        if (normalizedCode.Length is < 1 or > 64)
        {
            return Task.FromResult(Result<bool>.Failure(new Error(
                TenancyErrorCodes.EntitlementCodeInvalid,
                "Entitlement code is invalid.",
                ErrorType.Validation)));
        }

        return TenancyHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => EvaluateAsync(tenantId, normalizedCode, cancellationToken));
    }

    private async Task<Result<bool>> EvaluateAsync(
        Guid tenantId,
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        var catalog = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.FindCatalogByCode,
                Tenancy.Persistence.TenancySqlParameters.Create(("Code", normalizedCode)),
                cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null || !catalog.IsActive)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.EntitlementCodeInvalid,
                "Entitlement code is not registered.",
                ErrorType.Validation));
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

        var bindingCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantEntitlementSql.CountActiveFeatureBindings,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("EntitlementCode", normalizedCode),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (bindingCount < 1)
        {
            return Result<bool>.Failure(new Error(
                TenancyErrorCodes.EntitlementFeatureNotGranted,
                "The tenant is not entitled to this feature.",
                ErrorType.BusinessRule));
        }

        return Result<bool>.Success(true);
    }
}
