using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.GetCurrentUser;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me", (ClaimsPrincipal principal) =>
        {
            if (!Guid.TryParse(
                    principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                    out var userId)
                || !Guid.TryParse(
                    principal.FindFirstValue(IdentityClaimTypes.SessionId),
                    out var sessionId))
            {
                return Results.Unauthorized();
            }

            var tenantId = Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.TenantId),
                out var parsedTenantId)
                ? parsedTenantId
                : (Guid?)null;
            var response = new CurrentUserResponse(
                userId,
                principal.FindFirstValue("preferred_username") ?? string.Empty,
                principal.FindFirstValue(JwtRegisteredClaimNames.Name) ?? string.Empty,
                tenantId,
                principal.FindFirstValue(IdentityClaimTypes.Scope) ?? string.Empty,
                principal.FindAll(IdentityClaimTypes.Permission)
                    .Select(claim => claim.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                sessionId);
            return Results.Ok(response);
        })
        .WithTags("Identity")
        .RequireAuthorization();
    }
}
