namespace Full.NET.Modules.Identity.Oidc;

/// <summary>只有部署方登记的 HTTPS 来源可以接收通知；应用管理员不能扩大网络访问范围。</summary>
internal static class IdentityOidcBackchannelPolicy
{
    internal static bool IsAllowed(string? value, IReadOnlyCollection<string> allowedOrigins) =>
        string.IsNullOrWhiteSpace(value) || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.Query) && value.Length <= 2048
            && allowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal));
}
