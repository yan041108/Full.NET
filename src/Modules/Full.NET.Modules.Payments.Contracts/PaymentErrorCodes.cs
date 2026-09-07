namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付模块稳定业务错误码。</summary>
public static class PaymentErrorCodes
{
    /// <summary>商户配置不存在。</summary>
    public const string MerchantConfigNotFound = "payments.merchant_config.not_found";

    /// <summary>商户配置元数据校验失败。</summary>
    public const string MerchantConfigInvalid = "payments.merchant_config.invalid";

    /// <summary>商户配置并发版本冲突。</summary>
    public const string MerchantConfigConcurrencyConflict = "payments.merchant_config.concurrency_conflict";

    /// <summary>商户配置密钥必填。</summary>
    public const string MerchantConfigSecretRequired = "payments.merchant_config.secret_required";

    /// <summary>商户配置不可用。</summary>
    public const string MerchantConfigUnavailable = "payments.merchant_config.unavailable";

    /// <summary>支付订单不存在。</summary>
    public const string OrderNotFound = "payments.order.not_found";

    /// <summary>支付订单元数据校验失败。</summary>
    public const string OrderInvalid = "payments.order.invalid";

    /// <summary>支付订单渠道调用失败。</summary>
    public const string OrderProviderFailed = "payments.order.provider_failed";

    /// <summary>支付订单渠道调用结果未知，本地意图已提交且不得当作失败回滚。</summary>
    public const string OrderProviderUnknown = "payments.order.provider_unknown";

    /// <summary>租户不存在或不可用。</summary>
    public const string TenantNotFound = "payments.tenant.not_found";

    /// <summary>支付通知验签或解密失败。</summary>
    public const string NotifyInvalid = "payments.notify.invalid";

    /// <summary>支付通知业务校验失败。</summary>
    public const string NotifyRejected = "payments.notify.rejected";

    /// <summary>退款记录不存在。</summary>
    public const string RefundNotFound = "payments.refund.not_found";

    /// <summary>退款请求校验失败。</summary>
    public const string RefundInvalid = "payments.refund.invalid";

    /// <summary>退款渠道调用失败。</summary>
    public const string RefundProviderFailed = "payments.refund.provider_failed";

    /// <summary>退款渠道调用结果未知，本地意图已提交且不得当作失败回滚。</summary>
    public const string RefundProviderUnknown = "payments.refund.provider_unknown";

    /// <summary>已有退款正在调用渠道，并发请求不得再次产生副作用。</summary>
    public const string RefundInProgress = "payments.refund.in_progress";

    /// <summary>订单状态不允许当前操作。</summary>
    public const string OrderStateInvalid = "payments.order.state_invalid";
}
