namespace Full.NET.Modules.Payments.Contracts;

/// <summary>受支持的支付渠道键。</summary>
public static class PaymentChannelKeys
{
    /// <summary>微信 Native 扫码支付。</summary>
    public const string WeChatNative = "wechat_native";

    /// <summary>支付宝电脑网站支付（Page Pay）。</summary>
    public const string AlipayPage = "alipay_page";
}
