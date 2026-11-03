using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Auditing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>订阅报价、付款和退款的 Tenancy 权威边界；不查询 Payments 表或在本地事务中调用其他模块。</summary>
internal sealed partial class TenantSubscriptionBillingService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    TenantSubscriptionWriteBoundary boundary, TenantHostPackageBinder packageBinder,
    ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> audit) : ITenantSubscriptionBillingPort
{
    internal Task<Result<TenantSubscriptionPaymentTerms>> CreateAsync(Guid tenantId, CreatePaidTenantSubscriptionRequest request,
        CancellationToken token, Guid? actorUserId = null)
    {
        if (tenantId == Guid.Empty || request.PackageId == Guid.Empty || request.IdempotencyKey == Guid.Empty
            || request.OfferVersion < 1 || request.EffectiveFromUtc.Year is < 2000 or > 9988)
            return Task.FromResult(Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.invalid", ErrorType.Validation));
        // 两种正式提供程序共同保留微秒；首次响应、持久化与幂等重放必须使用相同精度。
        var utc = request.EffectiveFromUtc.ToUniversalTime();
        var start = utc.AddTicks(-(utc.Ticks % 10));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            request.PackageId.ToString("D"), request.OfferVersion.ToString(CultureInfo.InvariantCulture), start.ToString("O", CultureInfo.InvariantCulture)))));
        return transaction.ExecuteResultAsync(async local =>
        {
            if (!await boundary.EnterAsync(tenantId, local).ConfigureAwait(false))
                return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.NotFound, ErrorType.NotFound);
            var previous = await queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.FindRequest,
                TenancySqlParameters.Create(("TenantId", tenantId), ("RequestId", request.IdempotencyKey)), local).ConfigureAwait(false);
            if (previous is not null)
                return previous.RequestHash == hash ? Result<TenantSubscriptionPaymentTerms>.Success(previous.Terms())
                    : Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.request_conflict", ErrorType.Conflict);
            var count = await queries.QuerySingleOrDefaultAsync<long>(TenantSubscriptionSql.CountActive,
                TenancySqlParameters.Create(("TenantId", tenantId)), local).ConfigureAwait(false);
            if (count != 0) return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.SubscriptionStatusInvalid, ErrorType.Conflict);
            var phase = await queries.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(TenantEntitlementSql.GetEnforcementPhase,
                TenancySqlParameters.Create(("SettingsId", TenancySettingsConstants.SettingsId)), local).ConfigureAwait(false);
            if (phase?.EntitlementEnforcementPhase != TenantEntitlementEnforcementPhases.Enforced)
                return Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.enforcement_required", ErrorType.BusinessRule);
            var offer = await queries.QuerySingleOrDefaultAsync<TenantPackageOfferRecord>(database.Value.Provider == DatabaseProvider.MySql
                    ? TenantPackageOfferSql.LockMySql : TenantPackageOfferSql.LockSqlServer,
                TenancySqlParameters.Create(("Id", request.PackageId), ("Version", request.OfferVersion)), local).ConfigureAwait(false);
            if (offer is null) return Failure<TenantSubscriptionPaymentTerms>("tenancy.package_offer.version_conflict", ErrorType.Conflict);
            var package = await queries.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(TenantPackageSql.FindPackageById,
                TenancySqlParameters.Create(("PackageId", request.PackageId)), local).ConfigureAwait(false);
            if (package is null || !package.IsActive) return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.PackageInactive, ErrorType.BusinessRule);
            var end = start.AddDays(offer.DurationDays);
            if (end <= clock.UtcNow) return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.SubscriptionPeriodInvalid, ErrorType.Validation);
            // 不能把已有成员或文件的权威用量猜成零；缺少基线时先完成现有对账入口。
            foreach (var metric in new[] { TenantQuotaMetricCodes.IdentitySeats, TenantQuotaMetricCodes.FilesStorageBytes })
                if (await FindQuotaAsync(tenantId, metric, local).ConfigureAwait(false) is null)
                    return Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.quota_baseline_required", ErrorType.BusinessRule);
            var entitlements = await queries.QueryAsync<SubscriptionEntitlementRecord>(TenantPackageOfferSql.ListEntitlements,
                TenancySqlParameters.Create(("Id", offer.Id)), local).ConfigureAwait(false);
            if (entitlements.Count > 100 || entitlements.Any(e => !e.IsActive || e.EntitlementType != TenantEntitlementTypes.Feature))
                return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.EntitlementCodeInvalid, ErrorType.Validation);
            var subscription = ids.NewId();
            var operation = ids.NewId();
            var now = clock.UtcNow;
            if (await commands.ExecuteAsync(TenantSubscriptionSql.Insert, TenancySqlParameters.Create(("Id", subscription),
                ("TenantId", tenantId), ("PackageId", offer.Id), ("Status", TenantSubscriptionStatuses.PastDue),
                ("TrialEndsAtUtc", null), ("CurrentPeriodStartUtc", start), ("CurrentPeriodEndUtc", end),
                ("CreatedAtUtc", now), ("UpdatedAtUtc", now)), local).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Pending subscription was not persisted.");
            if (await commands.ExecuteAsync(SubscriptionInvoiceSql.Insert, TenancySqlParameters.Create(("Id", subscription),
                ("TenantId", tenantId), ("RequestId", request.IdempotencyKey), ("RequestHash", hash), ("PackageId", offer.Id),
                ("PackageVersion", package.Version), ("OfferVersion", offer.Version), ("PaymentTenantId", offer.PaymentTenantId),
                ("MerchantConfigId", offer.MerchantConfigId), ("PaymentOperationId", operation), ("AmountMinor", offer.AmountMinor),
                ("Currency", offer.Currency), ("SeatLimit", offer.SeatLimit), ("StorageLimit", offer.StorageLimit),
                ("EffectiveFromUtc", start), ("EffectiveToUtc", end), ("Subject", package.Name), ("Now", now)), local).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Subscription invoice was not persisted.");
            foreach (var entitlement in entitlements)
                if (await commands.ExecuteAsync(SubscriptionInvoiceSql.InsertEntitlement, TenancySqlParameters.Create(
                    ("Id", ids.NewId()), ("InvoiceId", subscription), ("EntitlementId", entitlement.Id),
                    ("EntitlementCode", entitlement.Code), ("Now", now)), local).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Subscription entitlement snapshot was not persisted.");
            await audit.WriteAsync(new(TenancyDomainAuditActionKeys.SubscriptionInvoiceCreated, subscription, tenantId,
                TenancyDomainAuditOutcomes.Success, actorUserId, null, null), local).ConfigureAwait(false);
            return Result<TenantSubscriptionPaymentTerms>.Success(new(subscription, tenantId, offer.PaymentTenantId, operation,
                offer.MerchantConfigId, offer.Id, offer.Version, offer.AmountMinor, offer.Currency, start, end, package.Name));
        }, token);
    }

    public async Task<Result<TenantSubscriptionPaymentTerms>> GetPaymentTermsAsync(Guid tenantId, Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (await queries.QuerySingleOrDefaultAsync<Guid>(SubscriptionInvoiceSql.ReadActiveTenant,
            TenancySqlParameters.Create(("TenantId", tenantId)), cancellationToken).ConfigureAwait(false) == Guid.Empty)
            return Failure<TenantSubscriptionPaymentTerms>(TenancyErrorCodes.NotFound, ErrorType.NotFound);
        var invoice = await queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", subscriptionId)), cancellationToken).ConfigureAwait(false);
        if (invoice is null) return Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.not_found", ErrorType.NotFound);
        var subscription = await queries.QuerySingleOrDefaultAsync<TenantSubscriptionRecord>(TenantSubscriptionSql.FindById,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", subscriptionId)), cancellationToken).ConfigureAwait(false);
        if (invoice.Phase is not ("Pending" or "Applied") || invoice.EffectiveToUtc <= clock.UtcNow
            || subscription?.Status is not (TenantSubscriptionStatuses.PastDue or TenantSubscriptionStatuses.Active))
            return Failure<TenantSubscriptionPaymentTerms>("tenancy.subscription_billing.closed", ErrorType.Conflict);
        return Result<TenantSubscriptionPaymentTerms>.Success(invoice.Terms());
    }

    private Task<TenantQuotaMetricRecord?> FindQuotaAsync(Guid tenantId, string code, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(TenantQuotaSql.FindMetric,
            TenancySqlParameters.Create(("TenantId", tenantId), ("MetricCode", code), ("PeriodKey", TenantQuotaDefaults.PeriodKey)), token);
    private static Result<T> Failure<T>(string code, ErrorType type) =>
        Result<T>.Failure(new Error(code, "The subscription payment cannot be applied to the requested terms.", type));
}
