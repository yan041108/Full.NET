using MemoryPack;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>本模块已验证的支付事实；不包含渠道密钥、回调原文或付款人的身份凭据。</summary>
[MemoryPackable]
internal partial record PaymentSucceededFact(Guid OrderId, Guid TenantId, Guid MerchantConfigId, Guid? CreationKey,
    long AmountMinor, string Currency, string ProviderTransactionId, DateTimeOffset PaidAtUtc)
{
    internal const string EventType = "fullnet.payments.order.paid";
}

/// <summary>实际成功退款及其原始资金归属；独立事件身份不被解释为新的支付。</summary>
[MemoryPackable]
internal partial record PaymentRefundSucceededFact(PaymentSucceededFact Payment, Guid RefundId, long RefundAmountMinor)
{
    internal const string EventType = "fullnet.payments.refund.succeeded";
}
