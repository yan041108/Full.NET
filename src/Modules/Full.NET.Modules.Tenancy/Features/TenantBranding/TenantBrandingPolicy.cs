using System.Text.RegularExpressions;

namespace Full.NET.Modules.Tenancy.Features.TenantBranding;

/// <summary>租户品牌字段校验策略；禁止客户端路径、可执行扩展名与 Logo URL 写回。</summary>
internal static partial class TenantBrandingPolicy
{
    public const int SystemTitleMaxLength = 128;
    public const int ContactPhoneMaxLength = 32;
    public const int ContactEmailMaxLength = 256;
    public const int ContactAddressMaxLength = 512;
    public const int CopyrightMaxLength = 256;
    public const long LogoMaxBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/svg+xml",
    };

    /// <summary>判断 Logo 内容类型是否允许上传。</summary>
    public static bool IsAllowedLogoContentType(string contentType) =>
        AllowedLogoContentTypes.Contains(NormalizeContentType(contentType));

    /// <summary>校验品牌文本字段是否满足长度与路径安全约束。</summary>
    public static bool TryValidateTextFields(
        string? systemTitle,
        string? contactPhone,
        string? contactEmail,
        string? contactAddress,
        string? copyright)
    {
        if (!IsOptionalLength(systemTitle, SystemTitleMaxLength)
            || !IsOptionalLength(contactPhone, ContactPhoneMaxLength)
            || !IsOptionalLength(contactEmail, ContactEmailMaxLength)
            || !IsOptionalLength(contactAddress, ContactAddressMaxLength)
            || !IsOptionalLength(copyright, CopyrightMaxLength))
        {
            return false;
        }

        return !ContainsForbiddenPathTokens(systemTitle)
               && !ContainsForbiddenPathTokens(contactPhone)
               && !ContainsForbiddenPathTokens(contactEmail)
               && !ContainsForbiddenPathTokens(contactAddress)
               && !ContainsForbiddenPathTokens(copyright)
               && (string.IsNullOrWhiteSpace(contactEmail)
                   || EmailPattern().IsMatch(contactEmail.Trim()));
    }

    private static bool IsOptionalLength(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength;

    private static bool ContainsForbiddenPathTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Contains('\\')
               || trimmed.StartsWith('/')
               || trimmed.Contains("file:", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
               || ExecutableExtensionPattern().IsMatch(trimmed);
    }

    private static string NormalizeContentType(string contentType) =>
        contentType.Split(';', 2)[0].Trim();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\.(exe|bat|cmd|msi|dll|sh|ps1)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutableExtensionPattern();
}
