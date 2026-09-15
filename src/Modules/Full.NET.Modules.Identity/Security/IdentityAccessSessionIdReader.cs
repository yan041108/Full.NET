using System.Security.Claims;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 从已验证主体读取访问会话标识；OIDC 访问令牌使用 ApplicationSessionId，旧 JWT 使用 SessionId。
/// </summary>
internal static class IdentityAccessSessionIdReader
{
    public static bool TryRead(
        ClaimsPrincipal principal,
        IdentityOidcOptions oidcOptions,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;
        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        if (oidcOptions.Enable
            && !string.IsNullOrWhiteSpace(oidcOptions.Issuer)
            && string.Equals(issuer, oidcOptions.Issuer, StringComparison.Ordinal))
        {
            return Guid.TryParse(
                principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId),
                out sessionId);
        }

        return Guid.TryParse(
            principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId),
            out sessionId);
    }
}