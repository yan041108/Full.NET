using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC protocol path detection; protocol endpoints must not use ProblemDetails envelopes.</summary>
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