using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>Tenancy 拥有报价和权益；Payments 只在自己的资金事实验证后调用，不与对方共享事务。</summary>
public interface ITenantSubscriptionBillingPort
{
    /// <summary>读取已冻结的待付款报价；不得接受调用方替换金额、币种或收款商户。</summary>
    /// <param name="tenantId">权益受益租户。</param>
    /// <param name="subscriptionId">本次订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>冻结报价或明确失败。</returns>
    Task<Result<TenantSubscriptionPaymentTerms>> GetPaymentTermsAsync(Guid tenantId, Guid subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>消费已验证的支付事实，幂等激活订阅并在 Tenancy 本地事务内授予冻结权益。</summary>
    /// <param name="receipt">Payments 权威状态生成的支付事实；不是浏览器回传的证明。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>履约完成或拒绝原因；重复事实不重复授予。</returns>
    Task<Result<bool>> ApplyVerifiedPaymentAsync(TenantSubscriptionPaymentReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>消费已验证退款，按原订阅关闭自动权益并保留账单及业务数据供人工核对。</summary>
    /// <param name="receipt">原支付绑定及实际退款事实。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款处理完成或拒绝原因。</returns>
    Task<Result<bool>> ApplyVerifiedRefundAsync(TenantSubscriptionRefundReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>由 Tenancy 生成的不可变报价；收款租户和权益受益租户明确分开。</summary>
/// <param name="SubscriptionId">待履约订阅。</param>
/// <param name="TenantId">权益受益租户。</param>
/// <param name="PaymentTenantId">拥有收款商户配置的租户。</param>
/// <param name="PaymentOperationId">支付创建幂等键，与最终渠道订单标识分开。</param>
/// <param name="MerchantConfigId">冻结收款商户。</param>
/// <param name="PackageId">冻结套餐。</param>
/// <param name="OfferVersion">冻结报价版本。</param>
/// <param name="AmountMinor">最小货币单位金额。</param>
/// <param name="Currency">规范大写币种。</param>
/// <param name="EffectiveFromUtc">权益生效时间。</param>
/// <param name="EffectiveToUtc">权益到期时间。</param>
/// <param name="Subject">冻结商品名称，重试不会随套餐改名或界面语言变化。</param>
public sealed record TenantSubscriptionPaymentTerms(Guid SubscriptionId, Guid TenantId, Guid PaymentTenantId,
    Guid PaymentOperationId, Guid MerchantConfigId, Guid PackageId, int OfferVersion, long AmountMinor,
    string Currency, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, string Subject);

/// <summary>已验证支付事实；金额、商户、收款和受益租户均须匹配原报价。</summary>
/// <param name="SubscriptionId">原订阅。</param>
/// <param name="TenantId">权益受益租户。</param>
/// <param name="PaymentTenantId">支付所属租户。</param>
/// <param name="PaymentOperationId">原支付创建身份。</param>
/// <param name="PaymentOrderId">实际资金订单。</param>
/// <param name="MerchantConfigId">实际收款商户。</param>
/// <param name="AmountMinor">实际支付金额。</param>
/// <param name="Currency">实际支付币种。</param>
/// <param name="ProviderTransactionId">已验证渠道交易标识。</param>
/// <param name="PaidAtUtc">资金成功时间。</param>
public sealed record TenantSubscriptionPaymentReceipt(Guid SubscriptionId, Guid TenantId, Guid PaymentTenantId,
    Guid PaymentOperationId, Guid PaymentOrderId, Guid MerchantConfigId, long AmountMinor, string Currency,
    string ProviderTransactionId, DateTimeOffset PaidAtUtc);

/// <summary>已验证退款事实；退款不会被解释为一次新付款或删除既有业务数据。</summary>
/// <param name="Payment">原支付绑定。</param>
/// <param name="RefundId">实际退款身份。</param>
/// <param name="RefundAmountMinor">实际成功退款金额。</param>
public sealed record TenantSubscriptionRefundReceipt(TenantSubscriptionPaymentReceipt Payment,
    Guid RefundId, long RefundAmountMinor);
