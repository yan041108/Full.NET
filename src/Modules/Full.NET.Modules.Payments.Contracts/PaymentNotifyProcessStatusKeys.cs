namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付通知处理状态键。</summary>
public static class PaymentNotifyProcessStatusKeys
{
    /// <summary>已成功处理并更新订单。</summary>
    public const string Processed = "processed";

    /// <summary>重复通知，已幂等忽略。</summary>
    public const string IgnoredDuplicate = "ignored_duplicate";

    /// <summary>校验失败或业务拒绝。</summary>
    public const string Rejected = "rejected";
}
