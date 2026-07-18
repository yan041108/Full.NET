using System.Security.Claims;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.GetNavigation;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/navigation",
                (
                    ClaimsPrincipal principal,
                    PermissionClaimEvaluator permissionClaimEvaluator,
                    NavigationProjector projector) =>
                {
                    var permissions = permissionClaimEvaluator.ResolvePermissions(principal);
                    return Results.Ok(projector.Project(permissions));
                })
            .WithTags("Identity")
            .RequireFullNetPermission(IdentityAuthorizationContributor.NavigationRead);
    }
}
