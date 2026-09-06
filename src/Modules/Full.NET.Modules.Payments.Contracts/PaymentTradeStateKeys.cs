namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付订单交易状态键。</summary>
public static class PaymentTradeStateKeys
{
    /// <summary>本地已创建，尚未调用渠道。</summary>
    public const string Created = "created";

    /// <summary>渠道已受理，等待用户支付。</summary>
    public const string AwaitingPayment = "awaiting_payment";

    /// <summary>支付成功。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>订单已关闭。</summary>
    public const string Closed = "closed";

    /// <summary>创建或渠道调用失败。</summary>
    public const string Failed = "failed";

    /// <summary>退款处理中。</summary>
    public const string Refunding = "refunding";

    /// <summary>已退款。</summary>
    public const string Refunded = "refunded";
}
