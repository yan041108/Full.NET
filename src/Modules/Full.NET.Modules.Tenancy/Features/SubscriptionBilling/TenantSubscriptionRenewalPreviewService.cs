using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>组合可信来源与同版本报价；预览不预留名额，不替代创建时的根锁校验。</summary>
internal sealed class TenantSubscriptionRenewalPreviewService(TenantSubscriptionBillingService billing,
    TenantPackageOfferService offers, IClock clock, IQueryExecutor queries)
{
    internal async Task<Result<TenantSubscriptionRenewalPreviewResponse>> GetAsync(Guid tenantId, Guid subscriptionId, CancellationToken token,
        Guid? targetPackageId = null)
    {
        var source = await billing.ReadRenewalSourceAsync(tenantId, subscriptionId, token).ConfigureAwait(false);
        if (!source.IsSuccess) return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(source.Error!);
        var period = source.Value!;
        var packageId = targetPackageId ?? period.PackageId!.Value;
        var package = packageId == Guid.Empty ? null : await queries.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
            TenantPackageSql.FindPackageById, TenancySqlParameters.Create(("PackageId", packageId)), token).ConfigureAwait(false);
        if (package is not { IsActive: true })
            return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(new Error(TenancyErrorCodes.PackageInactive,
                "The target package is not available for the next period.", ErrorType.BusinessRule));
        var offer = await offers.GetAsync(packageId, token).ConfigureAwait(false);
        if (!offer.IsSuccess) return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(offer.Error!);
        var now = clock.UtcNow;
        if (!offer.Value!.IsActive || period.CurrentPeriodEndUtc <= now)
            return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(new Error(TenancyErrorCodes.SubscriptionStatusInvalid,
                "The renewal source or current offer is unavailable.", ErrorType.Conflict));
        return Result<TenantSubscriptionRenewalPreviewResponse>.Success(new(tenantId, subscriptionId, period.PackageId!.Value,
            period.Version, period.CurrentPeriodEndUtc, offer.Value, now));
    }
}
