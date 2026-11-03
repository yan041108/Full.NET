using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>先重读资金，再在事务外调用 Tenancy；响应丢失后使用原报价与资金身份重试。</summary>
internal sealed class SubscriptionFulfillmentProcessor(IQueryExecutor queries, ICommandExecutor commands,
    ITenantSubscriptionBillingPort billing, IClock clock)
{
    internal async Task<Result<bool>> ProcessAsync(Guid id, CancellationToken token)
    {
        var work = await FindAsync(id, token).ConfigureAwait(false);
        if (work is null) return Failure("payments.subscription_binding_not_found");
        if (work.Phase is "Applied" or "Refunded") return Result<bool>.Success(true);
        var lease = Guid.CreateVersion7();
        if (await commands.ExecuteAsync(SubscriptionFulfillmentSql.Acquire, PaymentSqlParameters.Create(
            ("Id", id), ("LeaseId", lease), ("Now", clock.UtcNow), ("Until", clock.UtcNow.AddSeconds(45))), token).ConfigureAwait(false) != 1)
            return Result<bool>.Success(false);
        try
        {
            work = await FindAsync(id, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Fulfillment disappeared after lease acquisition.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var ct = deadline.Token;
            var funds = await queries.QuerySingleOrDefaultAsync<SubscriptionPaymentAuthorityRecord>(SubscriptionFulfillmentSql.ReadFunds,
                PaymentSqlParameters.Create(("OrderId", work.PaymentOrderId)), ct).ConfigureAwait(false);
            if (funds is null || !Matches(work, funds))
            {
                await SaveAsync(work, lease, "Review", "payments.subscription_funds_mismatch", ct).ConfigureAwait(false);
                return Failure("payments.subscription_funds_mismatch");
            }
            var receipt = new TenantSubscriptionPaymentReceipt(work.SubscriptionId, work.BeneficiaryTenantId, work.TenantId,
                work.Id, funds.Id, funds.MerchantConfigId, funds.AmountMinor, funds.Currency,
                funds.ProviderTransactionId!, funds.PaidAtUtc!.Value);
            Result<bool> applied;
            var completed = "Applied";
            if (work.Phase == "ReadyRefund")
            {
                var refund = await queries.QuerySingleOrDefaultAsync<SubscriptionRefundAuthorityRecord>(SubscriptionFulfillmentSql.ReadRefund,
                    PaymentSqlParameters.Create(("RefundId", work.LastRefundId)), ct).ConfigureAwait(false);
                if (refund is null || refund.Id != work.LastRefundId || refund.OrderId != funds.Id || refund.TenantId != funds.TenantId
                    || refund.MerchantConfigId != funds.MerchantConfigId || refund.AmountMinor != work.RefundAmountMinor
                    || refund.Currency != funds.Currency || refund.RefundStateKey != PaymentRefundStateKeys.Succeeded
                    || string.IsNullOrWhiteSpace(refund.ProviderRefundId) || funds.TradeStateKey != PaymentTradeStateKeys.Refunded)
                {
                    await SaveAsync(work, lease, "Review", "payments.subscription_refund_mismatch", ct).ConfigureAwait(false);
                    return Failure("payments.subscription_refund_mismatch");
                }
                applied = await billing.ApplyVerifiedRefundAsync(new(receipt, refund.Id, refund.AmountMinor), ct).ConfigureAwait(false);
                completed = "Refunded";
            }
            else
            {
                if (funds.TradeStateKey != PaymentTradeStateKeys.Succeeded)
                {
                    await SaveAsync(work, lease, work.Attempt >= 10 ? "Review" : work.Phase,
                        "payments.subscription_funds_not_settled", ct).ConfigureAwait(false);
                    return Result<bool>.Success(false);
                }
                applied = await billing.ApplyVerifiedPaymentAsync(receipt, ct).ConfigureAwait(false);
            }
            if (applied.IsSuccess && applied.Value)
            {
                await SaveAsync(work, lease, completed, null, ct).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }
            await SaveAsync(work, lease, work.Attempt >= 10 ? "Review" : work.Phase,
                applied.Error?.Code ?? "payments.subscription_fulfillment_pending", ct).ConfigureAwait(false);
            return applied;
        }
        catch (Exception) when (work.Attempt >= 10)
        {
            await SaveAsync(work, lease, "Review", "payments.subscription_recovery_failed", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(work.Attempt, 6))));
            await commands.ExecuteAsync(SubscriptionFulfillmentSql.Release, PaymentSqlParameters.Create(
                ("Id", id), ("LeaseId", lease), ("DueAtUtc", clock.UtcNow.Add(delay))), CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal Task<int> RetryAsync(Guid orderId, CancellationToken token) => commands.ExecuteAsync(SubscriptionFulfillmentSql.Retry,
        PaymentSqlParameters.Create(("OrderId", orderId), ("Now", clock.UtcNow)), token);

    private Task<SubscriptionFulfillmentRecord?> FindAsync(Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            PaymentSqlParameters.Create(("Id", id)), token);
    private async Task SaveAsync(SubscriptionFulfillmentRecord work, Guid lease, string phase, string? error, CancellationToken token)
    {
        if (await commands.ExecuteAsync(SubscriptionFulfillmentSql.Save, PaymentSqlParameters.Create(
            ("Id", work.Id), ("LeaseId", lease), ("ExpectedPhase", work.Phase), ("Phase", phase), ("ErrorCode", error),
            ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Fulfillment changed while a recovery call was in flight.");
    }
    private static bool Matches(SubscriptionFulfillmentRecord w, SubscriptionPaymentAuthorityRecord f) =>
        w.PaymentOrderId == f.Id && w.Id == f.CreationKey && w.TenantId == f.TenantId && w.MerchantConfigId == f.MerchantConfigId
        && w.AmountMinor == f.AmountMinor && w.Currency == f.Currency && w.ProviderTransactionId == f.ProviderTransactionId
        && !string.IsNullOrWhiteSpace(f.ProviderTransactionId) && f.PaidAtUtc is not null;
    private static Result<bool> Failure(string code) => Result<bool>.Failure(new Error(code, "Subscription fulfillment requires reconciliation.", ErrorType.Conflict));
}
