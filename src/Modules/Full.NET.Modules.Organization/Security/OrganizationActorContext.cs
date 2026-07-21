using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Organization.Security;

/// <summary>从已认证主体读取数据范围解析所需标识。</summary>
internal static class OrganizationActorContext
{
    internal static bool TryResolve(
        ClaimsPrincipal principal,
        out Guid userId,
        out bool isSuperAdministrator)
    {
        userId = default;
        isSuperAdministrator = bool.TryParse(
            principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator),
            out var enabled)
            && enabled;
        return Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);
    }
}
