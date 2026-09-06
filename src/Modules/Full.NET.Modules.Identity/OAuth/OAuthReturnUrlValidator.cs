using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.OAuth;

/// <summary>校验 OAuth 完成后的 returnUrl，防止开放重定向。</summary>
internal sealed class OAuthReturnUrlValidator(IOptions<IdentityOptions> options)
{
    private readonly IdentityOptions _options = options.Value;

    /// <summary>规范化并校验 returnUrl；失败时返回 <see langword="null"/>。</summary>
    /// <param name="returnUrl">客户端提交的 returnUrl。</param>
    /// <param name="requestOrigin">当前请求的 origin。</param>
    /// <returns>可安全重定向的 URL。</returns>
    public string? Normalize(string? returnUrl, string requestOrigin)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var trimmed = returnUrl.Trim();
        if (trimmed.StartsWith('/'))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return null;
        }

        if (!string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            requestOrigin.TrimEnd('/'),
        };
        foreach (var configured in _options.AllowedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                allowedOrigins.Add(configured.Trim().TrimEnd('/'));
            }
        }

        var targetOrigin = $"{absolute.Scheme}://{absolute.Authority}";
        return allowedOrigins.Contains(targetOrigin) ? trimmed : null;
    }

    /// <summary>向 returnUrl 追加查询参数。</summary>
    /// <param name="returnUrl">基础 URL。</param>
    /// <param name="name">查询参数名。</param>
    /// <param name="value">查询参数值。</param>
    /// <returns>带查询参数的 URL。</returns>
    public static string AppendQuery(string returnUrl, string name, string value)
    {
        var separator = returnUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{returnUrl}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }
}
