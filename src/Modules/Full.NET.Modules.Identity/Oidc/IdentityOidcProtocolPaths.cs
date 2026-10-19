using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 协议路径识别；协议端点不得套用 ProblemDetails 包络。</summary>
internal static class IdentityOidcProtocolPaths
{
    public static bool IsProtocolPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var value = path.Value!;
        return value.StartsWith("/connect/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase);
    }
}