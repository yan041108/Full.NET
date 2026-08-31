namespace Full.NET.Modules.Notifications.Domain;

/// <summary>收件端点验证状态的闭合目录。</summary>
internal static class NotificationRecipientEndpointStatuses
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Failed = "failed";
}

/// <summary>将端点原值收敛为可展示掩码，禁止把邮箱、手机号或 OpenId 原样返回。</summary>
internal static class NotificationRecipientEndpointMasker
{
    public static string Mask(string rawValue, string endpointKindKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        var value = rawValue.Trim();
        if (string.Equals(endpointKindKey, "email", StringComparison.OrdinalIgnoreCase)
            && value.Contains('@', StringComparison.Ordinal))
        {
            var at = value.IndexOf('@');
            var local = value[..at];
            var domain = value[(at + 1)..];
            var localHead = local.Length == 0 ? "*" : local[..1];
            return $"{localHead}***@{MaskDomain(domain)}";
        }

        if (value.Length <= 4)
        {
            return "****";
        }

        return $"****{value[^4..]}";
    }

    private static string MaskDomain(string domain)
    {
        var lastDot = domain.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == domain.Length - 1)
        {
            return "***";
        }

        return $"***.{domain[(lastDot + 1)..]}";
    }
}
