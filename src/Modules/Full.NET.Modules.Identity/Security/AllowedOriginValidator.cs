using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Security;

internal sealed class AllowedOriginValidator
{
    private readonly HashSet<string> _allowedOrigins;

    public AllowedOriginValidator(IOptions<IdentityOptions> options)
    {
        _allowedOrigins = options.Value.AllowedOrigins
            .Select(TryNormalize)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(
        string? origin,
        string requestOrigin,
        string? referer = null)
    {
        var normalizedOrigin = TryNormalize(origin);
        if (normalizedOrigin is not null)
        {
            var normalizedRequestOrigin = TryNormalize(requestOrigin);
            if (normalizedRequestOrigin is null)
            {
                return false;
            }

            return string.Equals(
                    normalizedOrigin,
                    normalizedRequestOrigin,
                    StringComparison.OrdinalIgnoreCase)
                || _allowedOrigins.Contains(normalizedOrigin);
        }

        // Vite 同源代理等场景下浏览器可能省略 Origin，回退 Referer 白名单校验。
        var normalizedReferer = TryNormalizeReferer(referer);
        return normalizedReferer is not null
            && _allowedOrigins.Contains(normalizedReferer);
    }

    private static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string? TryNormalizeReferer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
