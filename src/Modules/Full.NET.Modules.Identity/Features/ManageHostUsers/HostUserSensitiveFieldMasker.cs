namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

using Full.NET.Modules.Identity.Contracts;

/// <summary>将手机号与证件号收敛为可展示掩码，并识别客户端误提交的掩码占位值。</summary>
internal static class HostUserSensitiveFieldMasker
{
    /// <summary>将手机号掩码为仅保留末四位的展示值。</summary>
    /// <param name="value">规范 E.164 手机号。</param>
    public static string MaskPhoneNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Length <= 4 ? "****" : $"****{value[^4..]}";
    }

    /// <summary>将证件号码掩码为保留前三位与末四位的展示值。</summary>
    /// <param name="value">规范证件号码。</param>
    public static string MaskIdCardNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length <= 7)
        {
            return value.Length <= 4 ? "****" : $"****{value[^4..]}";
        }

        return $"{value[..3]}{new string('*', value.Length - 7)}{value[^4..]}";
    }

    /// <summary>判断值是否为手机号掩码占位，禁止写回数据库。</summary>
    public static bool LooksLikeMaskedPhoneNumber(string? value) =>
        value is not null
        && value.StartsWith("****", StringComparison.Ordinal)
        && value.Length >= 8;

    /// <summary>判断值是否为证件号掩码占位，禁止写回数据库。</summary>
    public static bool LooksLikeMaskedIdCardNumber(string? value) =>
        value is not null && value.Contains('*', StringComparison.Ordinal);
}

/// <summary>当前访问者对敏感档案字段的明文揭示能力。</summary>
/// <param name="CanRevealPhoneNumber">是否允许查看手机号明文。</param>
/// <param name="CanRevealIdCardNumber">是否允许查看证件号明文。</param>
internal readonly record struct HostUserSensitiveFieldRevealAccess(
    bool CanRevealPhoneNumber,
    bool CanRevealIdCardNumber)
{
    /// <summary>根据有效权限码解析敏感字段明文揭示能力。</summary>
    public static HostUserSensitiveFieldRevealAccess FromPermissions(
        IReadOnlyCollection<string> permissions)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        return new HostUserSensitiveFieldRevealAccess(
            granted.Contains(IdentityUserManagementPermissions.RevealPhoneNumber),
            granted.Contains(IdentityUserManagementPermissions.RevealIdCardNumber));
    }
}
