using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>先登记资金关联，再调用已有幂等订单入口；跨模块报价读取与渠道下单均位于本地事务外。</summary>
internal sealed class SubscriptionCheckoutService(ITenantSubscriptionBillingPort billing, IQueryExecutor queries,
    ICommandExecutor commands, ICommandTransaction transaction, PaymentOrderManagementService orders,
    PaymentOrderQueryService orderQueries, IClock clock)
{
    internal async Task<Result<SubscriptionCheckoutResponse>> CheckoutAsync(Guid tenantId, Guid subscriptionId, CancellationToken token)
    {
        var quote = await billing.GetPaymentTermsAsync(tenantId, subscriptionId, token).ConfigureAwait(false);
        if (!quote.IsSuccess) return Result<SubscriptionCheckoutResponse>.Failure(quote.Error!);
        var terms = quote.Value!;
        var merchant = await queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(PaymentMerchantConfigSql.FindById,
            PaymentSqlParameters.Create(("MerchantConfigId", terms.MerchantConfigId)), token).ConfigureAwait(false);
        if (merchant is null || !merchant.IsEnabled || merchant.TenantId != terms.PaymentTenantId)
            return Failure(PaymentErrorCodes.MerchantConfigUnavailable);
        try
        {
            var intent = await transaction.ExecuteResultAsync(async local =>
            {
                var existing = await FindAsync(terms.PaymentOperationId, local).ConfigureAwait(false);
                if (existing is not null) return Matches(existing, terms) ? Result<bool>.Success(true)
                    : Result<bool>.Failure(new Error("payments.subscription_binding_conflict", "The frozen subscription binding changed.", ErrorType.Conflict));
                if (await commands.ExecuteAsync(SubscriptionFulfillmentSql.Insert, PaymentSqlParameters.Create(
                    ("Id", terms.PaymentOperationId), ("SubscriptionId", terms.SubscriptionId), ("TenantId", terms.PaymentTenantId),
                    ("BeneficiaryTenantId", terms.TenantId), ("MerchantConfigId", terms.MerchantConfigId), ("AmountMinor", terms.AmountMinor),
                    ("Currency", terms.Currency), ("EffectiveFromUtc", terms.EffectiveFromUtc), ("EffectiveToUtc", terms.EffectiveToUtc),
                    ("Now", clock.UtcNow)), local).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Subscription checkout binding was not persisted.");
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            if (!intent.IsSuccess) return Result<SubscriptionCheckoutResponse>.Failure(intent.Error!);
        }
        catch (DataCommandException e) when (e.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var winner = await FindAsync(terms.PaymentOperationId, token).ConfigureAwait(false);
            if (winner is null || !Matches(winner, terms)) return Failure("payments.subscription_binding_conflict");
        }
        var created = await orders.CreateAsync(new(terms.PaymentTenantId, terms.MerchantConfigId, merchant.ChannelKey,
            terms.AmountMinor, terms.Currency, terms.Subject, null, terms.PaymentOperationId), token).ConfigureAwait(false);
        if (!created.IsSuccess && created.Error?.Code == PaymentErrorCodes.OrderProviderUnknown)
        {
            var known = await queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindByIdempotencyKey,
                PaymentSqlParameters.Create(("TenantId", terms.PaymentTenantId), ("IdempotencyKey", terms.PaymentOperationId)), token).ConfigureAwait(false);
            if (known is not null) created = await orderQueries.GetByIdAsync(known.Id, token).ConfigureAwait(false);
        }
        if (!created.IsSuccess) return Result<SubscriptionCheckoutResponse>.Failure(created.Error!);
        var order = created.Value!;
        await commands.ExecuteAsync(SubscriptionFulfillmentSql.Awaiting, PaymentSqlParameters.Create(
            ("Id", terms.PaymentOperationId), ("OrderId", order.Id), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        var work = await FindAsync(terms.PaymentOperationId, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Subscription checkout binding disappeared.");
        return Result<SubscriptionCheckoutResponse>.Success(new(subscriptionId, order, work.Phase, work.ErrorCode));
    }

    internal async Task<Result<SubscriptionFulfillmentStatusResponse>> StatusAsync(Guid orderId, CancellationToken token)
    {
        var work = await queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.FindOrder,
            PaymentSqlParameters.Create(("OrderId", orderId)), token).ConfigureAwait(false);
        return work is null ? Result<SubscriptionFulfillmentStatusResponse>.Failure(new Error(
            "payments.subscription_binding_not_found", "No subscription fulfillment is associated with the order.", ErrorType.NotFound))
            : Result<SubscriptionFulfillmentStatusResponse>.Success(new(work.SubscriptionId, work.BeneficiaryTenantId,
                work.PaymentOrderId, work.Phase, work.ErrorCode, work.Attempt, work.UpdatedAtUtc,
                work.EffectiveFromUtc, work.Phase is "ReadyPaid" or "ReadyRefund" ? work.DueAtUtc : null, clock.UtcNow));
    }

    private Task<SubscriptionFulfillmentRecord?> FindAsync(Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            PaymentSqlParameters.Create(("Id", id)), token);
    private static bool Matches(SubscriptionFulfillmentRecord w, TenantSubscriptionPaymentTerms t) =>
        w.Id == t.PaymentOperationId && w.SubscriptionId == t.SubscriptionId && w.TenantId == t.PaymentTenantId
        && w.BeneficiaryTenantId == t.TenantId && w.MerchantConfigId == t.MerchantConfigId && w.AmountMinor == t.AmountMinor
        && w.Currency == t.Currency && w.EffectiveFromUtc == t.EffectiveFromUtc && w.EffectiveToUtc == t.EffectiveToUtc;
    private static Result<SubscriptionCheckoutResponse> Failure(string code) =>
        Result<SubscriptionCheckoutResponse>.Failure(new Error(code, "The subscription checkout is unavailable or changed.", ErrorType.Conflict));
}
