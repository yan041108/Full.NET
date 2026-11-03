using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Auditing;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

internal sealed partial class TenantSubscriptionBillingService
{
    public Task<Result<bool>> ApplyVerifiedPaymentAsync(TenantSubscriptionPaymentReceipt receipt,
        CancellationToken cancellationToken = default) => transaction.ExecuteResultAsync(async local =>
    {
        if (!ValidReceipt(receipt)) return Failure<bool>("tenancy.subscription_billing.receipt_invalid", ErrorType.Validation);
        if (!await boundary.EnterAsync(receipt.TenantId, local).ConfigureAwait(false))
            return Failure<bool>(TenancyErrorCodes.NotFound, ErrorType.NotFound);
        var invoice = await LockInvoiceAsync(receipt.TenantId, receipt.SubscriptionId, local).ConfigureAwait(false);
        if (invoice is null) return Failure<bool>("tenancy.subscription_billing.not_found", ErrorType.NotFound);
        if (!Matches(invoice, receipt)) return Failure<bool>("tenancy.subscription_billing.receipt_mismatch", ErrorType.Conflict);
        if (invoice.Phase == "Applied") return Result<bool>.Success(true);
        if (invoice.Phase != "Pending") return Failure<bool>("tenancy.subscription_billing.closed", ErrorType.Conflict);
        if (invoice.EffectiveFromUtc > clock.UtcNow) return Failure<bool>("tenancy.subscription_billing.not_effective", ErrorType.Conflict);
        if (invoice.EffectiveToUtc <= clock.UtcNow) return Failure<bool>("tenancy.subscription_billing.expired", ErrorType.Conflict);
        var package = await queries.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(TenantPackageSql.FindPackageById,
            TenancySqlParameters.Create(("PackageId", invoice.PackageId)), local).ConfigureAwait(false);
        if (package is null || !package.IsActive) return Failure<bool>(TenancyErrorCodes.PackageInactive, ErrorType.BusinessRule);
        var p = ReceiptParameters(invoice, receipt);
        if (await commands.ExecuteAsync(SubscriptionInvoiceSql.ActivateSubscription, p, local).ConfigureAwait(false) != 1)
            return Failure<bool>(TenancyErrorCodes.SubscriptionStatusInvalid, ErrorType.Conflict);
        var snapshot = await queries.QueryAsync<InvoiceEntitlementRecord>(SubscriptionInvoiceSql.ListEntitlements,
            TenancySqlParameters.Create(("Id", invoice.Id)), local).ConfigureAwait(false);
        foreach (var entitlement in snapshot)
        {
            var grant = ReceiptParameters(invoice, receipt);
            grant.Add("BindingId", entitlement.Id);
            grant.Add("EntitlementId", entitlement.EntitlementId);
            grant.Add("EntitlementCode", entitlement.EntitlementCode);
            if (await commands.ExecuteAsync(SubscriptionInvoiceSql.GrantEntitlement, grant, local).ConfigureAwait(false) != 1)
                return Failure<bool>(TenancyErrorCodes.EntitlementCodeInvalid, ErrorType.Conflict);
        }
        foreach (var (code, limit) in new[] { (TenantQuotaMetricCodes.IdentitySeats, invoice.SeatLimit),
                     (TenantQuotaMetricCodes.FilesStorageBytes, invoice.StorageLimit) })
        {
            var metric = await FindQuotaAsync(invoice.TenantId, code, local).ConfigureAwait(false);
            if (metric is null) return Failure<bool>("tenancy.subscription_billing.quota_baseline_required", ErrorType.BusinessRule);
            var quota = TenancySqlParameters.Create(("Id", invoice.Id), ("TenantId", invoice.TenantId),
                ("MetricId", metric.Id), ("MetricVersion", metric.Version), ("MetricCode", code),
                ("PeriodKey", TenantQuotaDefaults.PeriodKey), ("LimitValue", limit), ("Now", clock.UtcNow));
            if (await commands.ExecuteAsync(SubscriptionInvoiceSql.UpdateQuota, quota, local).ConfigureAwait(false) != 1)
                return Failure<bool>("tenancy.subscription_billing.quota_conflict", ErrorType.Conflict);
        }
        var binding = await packageBinder.BindActivePackageAsync(invoice.TenantId, invoice.PackageId, local).ConfigureAwait(false);
        if (!binding.IsSuccess) return Result<bool>.Failure(binding.Error!);
        if (await commands.ExecuteAsync(SubscriptionInvoiceSql.Apply, p, local).ConfigureAwait(false) != 1)
            return Failure<bool>("tenancy.subscription_billing.version_conflict", ErrorType.Conflict);
        await audit.WriteAsync(new(TenancyDomainAuditActionKeys.SubscriptionPaymentApplied, invoice.Id, invoice.TenantId,
            TenancyDomainAuditOutcomes.Success, null, null, null), local).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }, cancellationToken);

    public Task<Result<bool>> ApplyVerifiedRefundAsync(TenantSubscriptionRefundReceipt receipt,
        CancellationToken cancellationToken = default) => transaction.ExecuteResultAsync(async local =>
    {
        var payment = receipt.Payment;
        if (!ValidReceipt(payment) || receipt.RefundId == Guid.Empty || receipt.RefundAmountMinor <= 0
            || receipt.RefundAmountMinor > payment.AmountMinor)
            return Failure<bool>("tenancy.subscription_billing.refund_invalid", ErrorType.Validation);
        if (!await boundary.EnterAsync(payment.TenantId, local, requireActive: false).ConfigureAwait(false))
            return Failure<bool>(TenancyErrorCodes.NotFound, ErrorType.NotFound);
        var invoice = await LockInvoiceAsync(payment.TenantId, payment.SubscriptionId, local).ConfigureAwait(false);
        if (invoice is null) return Failure<bool>("tenancy.subscription_billing.not_found", ErrorType.NotFound);
        if (!Matches(invoice, payment)) return Failure<bool>("tenancy.subscription_billing.receipt_mismatch", ErrorType.Conflict);
        if (invoice.Phase == "Refunded") return Result<bool>.Success(true);
        var p = ReceiptParameters(invoice, payment);
        p.Add("RefundId", receipt.RefundId);
        p.Add("RefundAmountMinor", receipt.RefundAmountMinor);
        if (await commands.ExecuteAsync(SubscriptionInvoiceSql.Refund, p, local).ConfigureAwait(false) != 1)
            return Failure<bool>("tenancy.subscription_billing.version_conflict", ErrorType.Conflict);
        // 只改变原订阅来源的资格；独立人工绑定、其他订阅、已有成员和文件均不清除。
        await commands.ExecuteAsync(SubscriptionInvoiceSql.MarkPastDue, p, local).ConfigureAwait(false);
        await audit.WriteAsync(new(TenancyDomainAuditActionKeys.SubscriptionRefundApplied, invoice.Id, invoice.TenantId,
            TenancyDomainAuditOutcomes.Success, null, null, null), local).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }, cancellationToken);

    private Task<SubscriptionInvoiceRecord?> LockInvoiceAsync(Guid tenantId, Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(database.Value.Provider == DatabaseProvider.MySql
                ? SubscriptionInvoiceSql.LockMySql : SubscriptionInvoiceSql.LockSqlServer,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", id)), token);

    private Dictionary<string, object?> ReceiptParameters(SubscriptionInvoiceRecord invoice, TenantSubscriptionPaymentReceipt receipt) =>
        TenancySqlParameters.Create(("Id", invoice.Id), ("TenantId", invoice.TenantId), ("PackageId", invoice.PackageId),
            ("Version", invoice.Version), ("EffectiveFromUtc", invoice.EffectiveFromUtc), ("EffectiveToUtc", invoice.EffectiveToUtc),
            ("PaymentOrderId", receipt.PaymentOrderId), ("ProviderTransactionId", receipt.ProviderTransactionId), ("Now", clock.UtcNow));

    private static bool ValidReceipt(TenantSubscriptionPaymentReceipt r) =>
        r.SubscriptionId != Guid.Empty && r.TenantId != Guid.Empty && r.PaymentTenantId != Guid.Empty
        && r.PaymentOperationId != Guid.Empty && r.PaymentOrderId != Guid.Empty && r.MerchantConfigId != Guid.Empty
        && r.AmountMinor > 0 && r.Currency is { Length: 3 } && !string.IsNullOrWhiteSpace(r.ProviderTransactionId)
        && r.ProviderTransactionId.Length <= 128 && r.PaidAtUtc != default;

    private static bool Matches(SubscriptionInvoiceRecord i, TenantSubscriptionPaymentReceipt r) =>
        i.Id == r.SubscriptionId && i.TenantId == r.TenantId && i.PaymentTenantId == r.PaymentTenantId
        && i.PaymentOperationId == r.PaymentOperationId && i.MerchantConfigId == r.MerchantConfigId
        && i.AmountMinor == r.AmountMinor && string.Equals(i.Currency, r.Currency, StringComparison.Ordinal)
        && (i.PaymentOrderId is null || i.PaymentOrderId == r.PaymentOrderId)
        && (i.ProviderTransactionId is null || string.Equals(i.ProviderTransactionId, r.ProviderTransactionId, StringComparison.Ordinal));
}
