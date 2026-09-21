using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
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
                async (
                    ClaimsPrincipal principal,
                    PermissionClaimEvaluator permissionClaimEvaluator,
                    NavigationProjector projector,
                    HostNavigationDefinitionLoader navigationLoader,
                    ICurrentTenant currentTenant,
                    CancellationToken cancellationToken) =>
                {
                    var permissions = permissionClaimEvaluator.ResolvePermissions(principal);
                    var additionalDefinitions = await navigationLoader
                        .LoadActiveDefinitionsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var isHostDataContext = currentTenant.IsAvailable && currentTenant.IsHost;
                    return Results.Ok(projector.Project(
                        permissions,
                        isHostDataContext,
                        additionalDefinitions));
                })
            .WithTags("Identity")
            .RequireFullNetPermission(IdentityAuthorizationContributor.NavigationRead);
    }
}
