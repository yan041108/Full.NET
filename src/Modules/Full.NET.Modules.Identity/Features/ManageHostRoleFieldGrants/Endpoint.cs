using System.Security.Claims;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.MapGroup("/api/v1/identity/field-projections")
            .WithTags("Identity");
        catalog.MapGet("/catalog", (HostRoleFieldGrantService service) =>
                Results.Ok(service.GetCatalog()))
            .Produces<IReadOnlyCollection<FieldProjectionResourceDefinition>>()
            .RequireFullNetPermission(IdentityRoleFieldGrantPermissions.Read);

        var roles = endpoints.MapGroup("/api/v1/identity/roles")
            .WithTags("Identity");
        roles.MapGet("/{roleId:guid}/field-grants", async (
            Guid roleId,
            string resourceKey,
            HostRoleFieldGrantService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(roleId, resourceKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostRoleFieldGrantsResponse>()
        .RequireFullNetPermission(IdentityRoleFieldGrantPermissions.Read);

        roles.MapPut("/{roleId:guid}/field-grants", async (
            Guid roleId,
            ReplaceHostRoleFieldGrantsRequest request,
            HostRoleFieldGrantService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ReplaceAsync(
                    roleId,
                    actorUserId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<HostRoleFieldGrantsResponse>()
        .RequireFullNetPermission(IdentityRoleFieldGrantPermissions.Replace);
    }

    private static bool TryGetSubject(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out userId)
            && userId != Guid.Empty;
    }
}
