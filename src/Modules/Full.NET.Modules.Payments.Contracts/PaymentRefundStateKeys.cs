namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付退款状态键。</summary>
public static class PaymentRefundStateKeys
{
    /// <summary>本地已创建，尚未调用渠道。</summary>
    public const string Created = "created";

    /// <summary>渠道处理中。</summary>
    public const string Processing = "processing";

    /// <summary>退款成功。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>退款失败。</summary>
    public const string Failed = "failed";

    /// <summary>退款已关闭。</summary>
    public const string Closed = "closed";
}
