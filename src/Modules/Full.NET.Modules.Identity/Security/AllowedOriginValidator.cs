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

    public bool IsAllowed(string? origin, string requestOrigin)
    {
        var normalizedOrigin = TryNormalize(origin);
        var normalizedRequestOrigin = TryNormalize(requestOrigin);
        if (normalizedOrigin is null || normalizedRequestOrigin is null)
        {
            return false;
        }

        return string.Equals(
                normalizedOrigin,
                normalizedRequestOrigin,
                StringComparison.OrdinalIgnoreCase)
            || _allowedOrigins.Contains(normalizedOrigin);
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
}
