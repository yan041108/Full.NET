using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>Outbox 本地消费只推进 Payments 工作项，不在消息事务里调用 Tenancy。</summary>
internal sealed class PaymentPaidFactHandler(IIntegrationEventSerializer serializer, IQueryExecutor queries,
    ICommandExecutor commands, IClock clock) : IIntegrationEventHandler
{
    public string EventType => PaymentSucceededFact.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var fact = serializer.Deserialize<PaymentSucceededFact>(payload);
        var work = await PaymentFinancialFactBinding.FindAsync(queries, fact, cancellationToken).ConfigureAwait(false);
        if (work is null) return;
        await commands.ExecuteAsync(SubscriptionFulfillmentSql.Paid,
            PaymentFinancialFactBinding.Parameters(fact, clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>退款优先于迟到付款；工作项保留原资金身份，重复退款不重开已结束履约。</summary>
internal sealed class PaymentRefundFactHandler(IIntegrationEventSerializer serializer, IQueryExecutor queries,
    ICommandExecutor commands, IClock clock) : IIntegrationEventHandler
{
    public string EventType => PaymentRefundSucceededFact.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var fact = serializer.Deserialize<PaymentRefundSucceededFact>(payload);
        var work = await PaymentFinancialFactBinding.FindAsync(queries, fact.Payment, cancellationToken).ConfigureAwait(false);
        if (work is null) return;
        if (fact.RefundId == Guid.Empty || fact.RefundAmountMinor <= 0 || fact.RefundAmountMinor > work.AmountMinor)
            throw new InvalidOperationException("Refund facts do not match the subscription payment.");
        var p = PaymentFinancialFactBinding.Parameters(fact.Payment, clock.UtcNow);
        p.Add("RefundId", fact.RefundId);
        p.Add("RefundAmountMinor", fact.RefundAmountMinor);
        await commands.ExecuteAsync(SubscriptionFulfillmentSql.Refunded, p, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>消息只可接续已经由报价登记的工作项；不根据事件临时猜测应授予哪个租户。</summary>
internal static class PaymentFinancialFactBinding
{
    internal static async Task<SubscriptionFulfillmentRecord?> FindAsync(IQueryExecutor queries, PaymentSucceededFact fact, CancellationToken token)
    {
        if (fact.CreationKey is null) return null;
        var work = await queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            PaymentSqlParameters.Create(("Id", fact.CreationKey)), token).ConfigureAwait(false);
        if (work is null) return null;
        if (work.TenantId != fact.TenantId || work.MerchantConfigId != fact.MerchantConfigId
            || work.AmountMinor != fact.AmountMinor || !string.Equals(work.Currency, fact.Currency, StringComparison.Ordinal)
            || (work.PaymentOrderId is not null && work.PaymentOrderId != fact.OrderId)
            || (work.ProviderTransactionId is not null && work.ProviderTransactionId != fact.ProviderTransactionId))
            throw new InvalidOperationException("Payment facts do not match the frozen subscription binding.");
        return work;
    }

    internal static Dictionary<string, object?> Parameters(PaymentSucceededFact fact, DateTimeOffset now) =>
        new(PaymentSqlParameters.Create(("Id", fact.CreationKey), ("OrderId", fact.OrderId),
            ("ProviderTransactionId", fact.ProviderTransactionId), ("PaidAtUtc", fact.PaidAtUtc), ("Now", now)));
}
