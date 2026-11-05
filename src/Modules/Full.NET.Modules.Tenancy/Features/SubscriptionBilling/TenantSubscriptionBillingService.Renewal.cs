using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

internal sealed partial class TenantSubscriptionBillingService
{
    /// <summary>预览可无锁读取；创建方已持有租户根锁，必须在该锁内重新调用。</summary>
    internal async Task<Result<TenantSubscriptionRecord>> ReadRenewalSourceAsync(Guid tenantId, Guid sourceId, CancellationToken token)
    {
        if (tenantId == Guid.Empty || sourceId == Guid.Empty
            || await queries.QuerySingleOrDefaultAsync<Guid>(SubscriptionInvoiceSql.ReadActiveTenant,
                TenancySqlParameters.Create(("TenantId", tenantId)), token).ConfigureAwait(false) == Guid.Empty)
            return Failure<TenantSubscriptionRecord>(TenancyErrorCodes.NotFound, ErrorType.NotFound);
        var phase = await queries.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(TenantEntitlementSql.GetEnforcementPhase,
            TenancySqlParameters.Create(("SettingsId", TenancySettingsConstants.SettingsId)), token).ConfigureAwait(false);
        if (phase?.EntitlementEnforcementPhase != TenantEntitlementEnforcementPhases.Enforced)
            return Failure<TenantSubscriptionRecord>("tenancy.subscription_billing.enforcement_required", ErrorType.BusinessRule);
        var source = await queries.QuerySingleOrDefaultAsync<TenantSubscriptionRecord>(TenantSubscriptionSql.FindById,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", sourceId)), token).ConfigureAwait(false);
        if (source is null) return Failure<TenantSubscriptionRecord>(TenancyErrorCodes.SubscriptionNotFound, ErrorType.NotFound);
        var invoice = await queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", sourceId)), token).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (source.Status is not (TenantSubscriptionStatuses.Active or TenantSubscriptionStatuses.Cancelled)
            || source.CurrentPeriodStartUtc > now || source.CurrentPeriodEndUtc <= now
            || invoice is not { Phase: "Applied" } || source.PackageId != invoice.PackageId
            || source.CurrentPeriodStartUtc != invoice.EffectiveFromUtc || source.CurrentPeriodEndUtc != invoice.EffectiveToUtc)
            return Failure<TenantSubscriptionRecord>(TenancyErrorCodes.SubscriptionStatusInvalid, ErrorType.Conflict);
        // 来源资格由原已付款周期决定；目标套餐的销售状态由预览和创建分别复核。
        return Result<TenantSubscriptionRecord>.Success(source);
    }
}
