using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>资金状态与重要事实同事务入 Outbox；消费者只持久化本模块工作项，履约 Contract 在事务外执行。</summary>
internal sealed class PaymentFinancialFactPublisher(IOutboxWriter outbox)
{
    internal Task PaidAsync(PaymentOrderRecord order, string? transactionId, DateTimeOffset paidAt, CancellationToken token)
    {
        var fact = new PaymentSucceededFact(order.Id, order.TenantId, order.MerchantConfigId, order.IdempotencyKey,
            order.AmountMinor, order.Currency, transactionId ?? string.Empty, paidAt);
        Validate(fact);
        return outbox.AddAsync(PaymentSucceededFact.EventType, 1, fact, token);
    }

    internal Task RefundedAsync(PaymentRefundSucceededFact fact, CancellationToken token)
    {
        Validate(fact.Payment);
        if (fact.RefundId == Guid.Empty || fact.RefundAmountMinor <= 0 || fact.RefundAmountMinor > fact.Payment.AmountMinor)
            throw new InvalidOperationException("Verified refund facts are incomplete.");
        return outbox.AddAsync(PaymentRefundSucceededFact.EventType, 1, fact, token);
    }

    private static void Validate(PaymentSucceededFact fact)
    {
        if (fact.OrderId == Guid.Empty || fact.TenantId == Guid.Empty || fact.MerchantConfigId == Guid.Empty
            || fact.AmountMinor <= 0 || fact.Currency.Length != 3 || string.IsNullOrWhiteSpace(fact.ProviderTransactionId)
            || fact.ProviderTransactionId.Length > 128 || fact.PaidAtUtc == default)
            throw new InvalidOperationException("Verified payment facts are incomplete.");
    }
}
