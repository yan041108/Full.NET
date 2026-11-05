using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>仅回读原冻结账单，不复用带付款资格检查的端口，也不按当前报价重算历史。</summary>
internal sealed class TenantSubscriptionInvoiceQueryService(IQueryExecutor queries, IClock clock)
{
    internal async Task<Result<TenantSubscriptionInvoiceResponse>> GetAsync(Guid tenantId, Guid subscriptionId, CancellationToken token)
    {
        // 路由可解析空 UUID，但持久化边界只接受已分配标识；只读缺失统一返回 404。
        if (tenantId == Guid.Empty || subscriptionId == Guid.Empty) return NotFound();
        var row = await queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Id", subscriptionId)), token).ConfigureAwait(false);
        if (row is null) return NotFound();
        return Result<TenantSubscriptionInvoiceResponse>.Success(new(row.Id, row.TenantId, row.PackageId, row.Subject,
            row.OfferVersion, row.AmountMinor.ToString(CultureInfo.InvariantCulture), row.Currency,
            row.SeatLimit.ToString(CultureInfo.InvariantCulture), row.StorageLimit.ToString(CultureInfo.InvariantCulture),
            row.EffectiveFromUtc, row.EffectiveToUtc, row.Phase, row.RenewalOfSubscriptionId,
            row.RenewalSourceVersion, row.PaymentOrderId, clock.UtcNow));
    }

    private static Result<TenantSubscriptionInvoiceResponse> NotFound() => Result<TenantSubscriptionInvoiceResponse>.Failure(
        new Error("tenancy.subscription_billing.not_found", "The original subscription invoice was not found.", ErrorType.NotFound));
}
