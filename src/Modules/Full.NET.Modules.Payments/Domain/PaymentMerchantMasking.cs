namespace Full.NET.Modules.Payments.Domain;

/// <summary>支付商户配置列表脱敏辅助。</summary>
internal static class PaymentMerchantMasking
{
    /// <summary>脱敏通用标识显示文本。</summary>
    /// <param name="value">原始值。</param>
    /// <returns>脱敏后的文本。</returns>
    public static string MaskIdentifier(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length <= 4)
        {
            return "***";
        }

        return normalized[..2] + "***" + normalized[^2..];
    }

    /// <summary>脱敏回调地址显示文本。</summary>
    /// <param name="notifyUrl">回调地址。</param>
    /// <returns>脱敏后的 URL。</returns>
    public static string MaskNotifyUrl(string notifyUrl)
    {
        if (!Uri.TryCreate(notifyUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return "***";
        }

        var host = uri.Host;
        var maskedHost = host.Length <= 4
            ? "***"
            : host[..2] + "***" + host[^1];
        return $"{uri.Scheme}://{maskedHost}{uri.PathAndQuery}";
    }
}
