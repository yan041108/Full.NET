using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>组合可信来源与同版本报价；预览不预留名额，不替代创建时的根锁校验。</summary>
internal sealed class TenantSubscriptionRenewalPreviewService(TenantSubscriptionBillingService billing,
    TenantPackageOfferService offers, IClock clock)
{
    internal async Task<Result<TenantSubscriptionRenewalPreviewResponse>> GetAsync(Guid tenantId, Guid subscriptionId, CancellationToken token)
    {
        var source = await billing.ReadRenewalSourceAsync(tenantId, subscriptionId, token).ConfigureAwait(false);
        if (!source.IsSuccess) return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(source.Error!);
        var offer = await offers.GetAsync(source.Value!.PackageId!.Value, token).ConfigureAwait(false);
        if (!offer.IsSuccess) return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(offer.Error!);
        var now = clock.UtcNow;
        if (!offer.Value!.IsActive || source.Value.CurrentPeriodEndUtc <= now)
            return Result<TenantSubscriptionRenewalPreviewResponse>.Failure(new Error(TenancyErrorCodes.SubscriptionStatusInvalid,
                "The renewal source or current offer is unavailable.", ErrorType.Conflict));
        return Result<TenantSubscriptionRenewalPreviewResponse>.Success(new(tenantId, subscriptionId,
            source.Value.Version, source.Value.CurrentPeriodEndUtc, offer.Value, now));
    }
}
