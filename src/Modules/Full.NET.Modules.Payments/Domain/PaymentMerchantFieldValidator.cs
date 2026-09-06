using Full.NET.Modules.Payments.Contracts;

namespace Full.NET.Modules.Payments.Domain;

/// <summary>支付商户配置字段校验。</summary>
internal static class PaymentMerchantFieldValidator
{
    /// <summary>名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>应用标识允许的最大字符数。</summary>
    internal const int MaxAppIdLength = 64;

    /// <summary>商户号允许的最大字符数。</summary>
    internal const int MaxMerchantIdLength = 32;

    /// <summary>证书序列号允许的最大字符数。</summary>
    internal const int MaxCertificateSerialNoLength = 64;

    /// <summary>回调地址允许的最大字符数。</summary>
    internal const int MaxNotifyUrlLength = 512;

    /// <summary>同步跳转地址允许的最大字符数。</summary>
    internal const int MaxReturnUrlLength = 512;

    /// <summary>支付宝可选字段占位符。</summary>
    internal const string AlipayOptionalPlaceholder = "-";

    /// <summary>默认货币代码。</summary>
    internal const string DefaultCurrency = "CNY";

    /// <summary>校验渠道键是否在受支持白名单内。</summary>
    /// <param name="channelKey">渠道键。</param>
    /// <returns>是否受支持。</returns>
    public static bool IsSupportedChannel(string? channelKey) =>
        string.Equals(channelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal)
        || string.Equals(channelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal);

    /// <summary>校验商户配置元数据。</summary>
    /// <param name="name">显示名称。</param>
    /// <param name="channelKey">渠道键。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="merchantId">商户号。</param>
    /// <param name="certificateSerialNo">证书序列号。</param>
    /// <param name="notifyUrl">回调地址。</param>
    /// <param name="returnUrl">同步跳转地址。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateMetadata(
        string name,
        string channelKey,
        string appId,
        string merchantId,
        string certificateSerialNo,
        string notifyUrl,
        string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return "Name is required and must not exceed 128 characters.";
        }

        if (!IsSupportedChannel(channelKey))
        {
            return "Channel key must be wechat_native or alipay_page.";
        }

        if (string.IsNullOrWhiteSpace(appId) || appId.Trim().Length > MaxAppIdLength)
        {
            return "App id is required and must not exceed 64 characters.";
        }

        if (string.Equals(channelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal))
        {
            return ValidateWeChatMetadata(merchantId, certificateSerialNo, notifyUrl);
        }

        return ValidateAlipayMetadata(merchantId, certificateSerialNo, notifyUrl, returnUrl);
    }

    /// <summary>校验创建订单请求。</summary>
    /// <param name="amountMinor">订单金额。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="subject">商品标题。</param>
    /// <param name="description">商品描述。</param>
    /// <param name="channelKey">可选渠道键。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateOrderRequest(
        long amountMinor,
        string currency,
        string subject,
        string? description,
        string? channelKey = null)
    {
        if (amountMinor <= 0)
        {
            return "Amount must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length > 8)
        {
            return "Currency is required and must not exceed 8 characters.";
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 128)
        {
            return "Subject is required and must not exceed 128 characters.";
        }

        if (description is not null && description.Trim().Length > 256)
        {
            return "Description must not exceed 256 characters.";
        }

        if (channelKey is not null
            && !string.IsNullOrWhiteSpace(channelKey)
            && !IsSupportedChannel(channelKey))
        {
            return "Channel key must be wechat_native or alipay_page.";
        }

        return null;
    }

    /// <summary>校验商户私钥 PEM 格式。</summary>
    /// <param name="privateKeyPem">私钥 PEM。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidatePrivateKeyPem(string privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            return "Private key PEM is required.";
        }

        var normalized = privateKeyPem.Trim();
        if (!normalized.Contains("BEGIN", StringComparison.Ordinal)
            || !normalized.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            return "Private key must be provided in PEM format.";
        }

        return null;
    }

    /// <summary>将支付宝可选字段规范化为占位符或裁剪后的值。</summary>
    /// <param name="value">原始值。</param>
    /// <returns>规范化后的值。</returns>
    public static string NormalizeAlipayOptionalField(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? AlipayOptionalPlaceholder : normalized;
    }

    /// <summary>将同步跳转地址规范化为可空字符串。</summary>
    /// <param name="returnUrl">原始跳转地址。</param>
    /// <returns>空字符串转为 null；否则返回裁剪后的值。</returns>
    public static string? NormalizeReturnUrl(string? returnUrl)
    {
        var normalized = returnUrl?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? ValidateWeChatMetadata(
        string merchantId,
        string certificateSerialNo,
        string notifyUrl)
    {
        if (string.IsNullOrWhiteSpace(merchantId) || merchantId.Trim().Length > MaxMerchantIdLength)
        {
            return "Merchant id is required and must not exceed 32 characters.";
        }

        if (string.IsNullOrWhiteSpace(certificateSerialNo)
            || certificateSerialNo.Trim().Length > MaxCertificateSerialNoLength)
        {
            return "Certificate serial number is required and must not exceed 64 characters.";
        }

        if (!IsSafeHttpsUrl(notifyUrl, MaxNotifyUrlLength))
        {
            return "Notify URL must be an absolute https URL without credentials.";
        }

        return null;
    }

    private static string? ValidateAlipayMetadata(
        string merchantId,
        string certificateSerialNo,
        string notifyUrl,
        string returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(merchantId) && merchantId.Trim().Length > MaxMerchantIdLength)
        {
            return "Merchant id must not exceed 32 characters.";
        }

        if (!string.IsNullOrWhiteSpace(certificateSerialNo)
            && certificateSerialNo.Trim().Length > MaxCertificateSerialNoLength)
        {
            return "Certificate serial number must not exceed 64 characters.";
        }

        if (!IsSafeHttpsUrl(notifyUrl, MaxNotifyUrlLength))
        {
            return "Notify URL must be an absolute https URL without credentials.";
        }

        if (!IsSafeHttpsUrl(returnUrl, MaxReturnUrlLength))
        {
            return "Return URL must be an absolute https URL without credentials.";
        }

        return null;
    }

    private static bool IsSafeHttpsUrl(string url, int maxLength)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Host.Length > 0 && url.Trim().Length <= maxLength;
    }
}
