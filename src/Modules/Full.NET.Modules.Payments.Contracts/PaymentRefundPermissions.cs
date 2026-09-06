namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付退款权限码。</summary>
public static class PaymentRefundPermissions
{
    /// <summary>读取退款记录。</summary>
    public const string Read = "payments.refunds.read";

    /// <summary>创建退款。</summary>
    public const string Create = "payments.refunds.create";
}
