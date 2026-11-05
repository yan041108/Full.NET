using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>Payments 只写自己的资金关联和恢复状态；不 JOIN 或更新 Tenancy 业务表。</summary>
internal static class SubscriptionFulfillmentSql
{
    private const string Columns = "Id,SubscriptionId,TenantId,BeneficiaryTenantId,MerchantConfigId,AmountMinor,Currency,EffectiveFromUtc,EffectiveToUtc,PaymentOrderId,ProviderTransactionId,PaidAtUtc,LastRefundId,RefundAmountMinor,Phase,ErrorCode,DueAtUtc,LeaseId,LeaseUntilUtc,Attempt,CreatedAtUtc,UpdatedAtUtc,Version";
    internal static readonly SqlStatement Find = new("payments.subscription_fulfillment.find",
        $"SELECT {Columns} FROM fn_payment_subscription_fulfillment WHERE Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindOrder = new("payments.subscription_fulfillment.find_order",
        $"SELECT {Columns} FROM fn_payment_subscription_fulfillment WHERE PaymentOrderId=@OrderId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Insert = new("payments.subscription_fulfillment.insert", $"""
        INSERT INTO fn_payment_subscription_fulfillment ({Columns}) VALUES
          (@Id,@SubscriptionId,@TenantId,@BeneficiaryTenantId,@MerchantConfigId,@AmountMinor,@Currency,@EffectiveFromUtc,@EffectiveToUtc,
           NULL,NULL,NULL,NULL,0,'Creating',NULL,@Now,NULL,NULL,0,@Now,@Now,1)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Awaiting = new("payments.subscription_fulfillment.awaiting", """
        UPDATE fn_payment_subscription_fulfillment SET PaymentOrderId=@OrderId,Phase='Awaiting',UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND Phase='Creating' AND (PaymentOrderId IS NULL OR PaymentOrderId=@OrderId)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Paid = new("payments.subscription_fulfillment.paid", """
        UPDATE fn_payment_subscription_fulfillment SET Phase='ReadyPaid',PaymentOrderId=@OrderId,
          ProviderTransactionId=@ProviderTransactionId,PaidAtUtc=@PaidAtUtc,ErrorCode=NULL,
          DueAtUtc=CASE WHEN EffectiveFromUtc>@Now THEN EffectiveFromUtc ELSE @Now END,Attempt=0,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND Phase IN ('Creating','Awaiting')
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Refunded = new("payments.subscription_fulfillment.refunded", """
        UPDATE fn_payment_subscription_fulfillment SET Phase='ReadyRefund',PaymentOrderId=@OrderId,
          ProviderTransactionId=@ProviderTransactionId,PaidAtUtc=@PaidAtUtc,LastRefundId=@RefundId,RefundAmountMinor=@RefundAmountMinor,
          ErrorCode=NULL,DueAtUtc=@Now,Attempt=0,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND Phase NOT IN ('ReadyRefund','Refunded')
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Acquire = new("payments.subscription_fulfillment.acquire", """
        UPDATE fn_payment_subscription_fulfillment SET LeaseId=@LeaseId,LeaseUntilUtc=@Until,Attempt=Attempt+1,Version=Version+1
        WHERE Id=@Id AND Phase IN ('ReadyPaid','ReadyRefund') AND DueAtUtc<=@Now
          AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Save = new("payments.subscription_fulfillment.save", """
        UPDATE fn_payment_subscription_fulfillment SET Phase=@Phase,ErrorCode=@ErrorCode,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND LeaseId=@LeaseId AND Phase=@ExpectedPhase
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Release = new("payments.subscription_fulfillment.release", """
        UPDATE fn_payment_subscription_fulfillment SET LeaseId=NULL,LeaseUntilUtc=NULL,DueAtUtc=@DueAtUtc,Version=Version+1
        WHERE Id=@Id AND LeaseId=@LeaseId
        """, SqlDataScope.HostOnly);
    // 付款复核仍须等待冻结生效日；退款复核不能被同一个未来时间延后。
    internal static readonly SqlStatement Retry = new("payments.subscription_fulfillment.retry", """
        UPDATE fn_payment_subscription_fulfillment SET Phase=CASE WHEN LastRefundId IS NULL THEN 'ReadyPaid' ELSE 'ReadyRefund' END,
          DueAtUtc=CASE WHEN LastRefundId IS NULL AND EffectiveFromUtc>@Now THEN EffectiveFromUtc ELSE @Now END,
          Attempt=0,ErrorCode=NULL,Version=Version+1
        WHERE PaymentOrderId=@OrderId AND Phase='Review' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueSqlServer = new("payments.subscription_fulfillment.due.sql_server", """
        SELECT TOP (20) Id FROM fn_payment_subscription_fulfillment
        WHERE Phase IN ('ReadyPaid','ReadyRefund') AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        ORDER BY DueAtUtc,Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement DueMySql = new("payments.subscription_fulfillment.due.my_sql", """
        SELECT Id FROM fn_payment_subscription_fulfillment
        WHERE Phase IN ('ReadyPaid','ReadyRefund') AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now)
        ORDER BY DueAtUtc,Id LIMIT 20
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadFunds = new("payments.subscription_fulfillment.read_funds", """
        SELECT Id,TenantId,MerchantConfigId,IdempotencyKey AS CreationKey,AmountMinor,Currency,TradeStateKey,ProviderTransactionId,PaidAtUtc
        FROM fn_payment_order WHERE Id=@OrderId
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadRefund = new("payments.subscription_fulfillment.read_refund", """
        SELECT Id,OrderId,TenantId,MerchantConfigId,AmountMinor,Currency,RefundStateKey,ProviderRefundId
        FROM fn_payment_refund WHERE Id=@RefundId
        """, SqlDataScope.HostOnly);
}

/// <summary>恢复工作项固定列序；商户、币种、金额及受益租户一经创建不可变。</summary>
internal sealed record SubscriptionFulfillmentRecord(Guid Id, Guid SubscriptionId, Guid TenantId, Guid BeneficiaryTenantId,
    Guid MerchantConfigId, long AmountMinor, string Currency, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc,
    Guid? PaymentOrderId, string? ProviderTransactionId, DateTimeOffset? PaidAtUtc, Guid? LastRefundId, long RefundAmountMinor,
    string Phase, string? ErrorCode, DateTimeOffset DueAtUtc, Guid? LeaseId, DateTimeOffset? LeaseUntilUtc,
    int Attempt, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Version);

/// <summary>事务外履约前重读本模块资金权威；退款中的订单不得作为新履约凭据。</summary>
internal sealed record SubscriptionPaymentAuthorityRecord(Guid Id, Guid TenantId, Guid MerchantConfigId, Guid? CreationKey,
    long AmountMinor, string Currency, string TradeStateKey, string? ProviderTransactionId, DateTimeOffset? PaidAtUtc);

/// <summary>实际成功退款权威投影；不能只凭投递消息就撤销权益。</summary>
internal sealed record SubscriptionRefundAuthorityRecord(Guid Id, Guid OrderId, Guid TenantId, Guid MerchantConfigId,
    long AmountMinor, string Currency, string RefundStateKey, string? ProviderRefundId);
