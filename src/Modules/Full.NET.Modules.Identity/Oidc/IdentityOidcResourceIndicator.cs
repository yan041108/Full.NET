using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>资源令牌只能由当前客户端登记的唯一资源指示符请求。</summary>
internal static class IdentityOidcResourceIndicator
{
    internal static bool IsAllowed(OpenIddictRequest request, string? registeredAudience, string defaultAudience)
    {
        var resources = request.GetResources();
        if (string.IsNullOrWhiteSpace(registeredAudience)
            || string.Equals(registeredAudience, defaultAudience, StringComparison.Ordinal)
            || !Uri.TryCreate(registeredAudience, UriKind.Absolute, out _))
            return resources.Length == 0;
        return resources.Length == 1 && string.Equals(resources[0], registeredAudience, StringComparison.Ordinal);
    }
}
