namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付订单权限码。</summary>
public static class PaymentOrderPermissions
{
    /// <summary>读取支付订单。</summary>
    public const string Read = "payments.orders.read";

    /// <summary>创建支付订单。</summary>
    public const string Create = "payments.orders.create";

    /// <summary>与渠道对账并同步订单状态。</summary>
    public const string Reconcile = "payments.orders.reconcile";
}
