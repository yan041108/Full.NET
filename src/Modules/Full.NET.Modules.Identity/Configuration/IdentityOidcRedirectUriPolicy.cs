namespace Full.NET.Modules.Identity.Configuration;

/// <summary>
/// OIDC 鍥炶皟鍦板潃瑙勫垯锛氫粎鎺ュ彈瀹屾暣缁濆 URI锛屾嫆缁濋€氶厤绗︺€佺浉瀵硅矾寰勪笌鍚煡璇㈢墖娈电殑鍦板潃銆?/// </summary>
internal static class IdentityOidcRedirectUriPolicy
{
    internal static bool IsExactAbsoluteUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('*', StringComparison.Ordinal)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return true;
    }

    internal static bool IsRegisteredRedirectUri(
        string? redirectUri,
        IReadOnlyCollection<string> registeredUris)
    {
        if (!IsExactAbsoluteUri(redirectUri))
        {
            return false;
        }

        return registeredUris.Contains(
            redirectUri!.TrimEnd('/'),
            StringComparer.Ordinal);
    }
}