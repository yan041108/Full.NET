using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.GetAuthorizationTree;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/identity/authorization-tree",
                (AuthorizationTreeProjector projector) =>
                    Results.Ok(projector.ProjectHostTree()))
            .WithTags("Identity")
            .RequireFullNetPermission(IdentityRoleManagementPermissions.Read)
            .Produces<AuthorizationTreeModuleResponse[]>(StatusCodes.Status200OK);
    }
}