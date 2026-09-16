using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageOidcClients;

internal static class OidcManagementEndpointSupport
{
    internal static bool TryResolveActor(HttpContext httpContext, out OidcManagementActorContext actor)
    {
        actor = default!;
        if (!TryGetSubject(httpContext.User, out var actorUserId))
        {
            return false;
        }

        actor = new OidcManagementActorContext(
            actorUserId,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());
        return true;
    }

    private static bool TryGetSubject(System.Security.Claims.ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out userId)
            && userId != Guid.Empty;
    }
}