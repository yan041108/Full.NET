namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付商户配置权限码。</summary>
public static class PaymentMerchantPermissions
{
    /// <summary>读取商户配置。</summary>
    public const string Read = "payments.merchants.read";

    /// <summary>创建商户配置。</summary>
    public const string Create = "payments.merchants.create";

    /// <summary>更新商户配置。</summary>
    public const string Update = "payments.merchants.update";
}
