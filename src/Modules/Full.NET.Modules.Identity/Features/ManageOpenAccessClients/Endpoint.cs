using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/open-access-clients")
            .WithTags("IdentityOpenAccessClients");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? userId,
            string? nameContains,
            OpenAccessClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    nameContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListOpenAccessClients")
        .Produces<PagedResult<OpenAccessClientResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Read);

        group.MapGet("/{clientId:guid}", async (
            Guid clientId,
            OpenAccessClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(clientId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetOpenAccessClient")
        .Produces<OpenAccessClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Read);

        group.MapPost("/", async (
            CreateOpenAccessClientRequest request,
            OpenAccessClientManagementService service,
            PermissionClaimEvaluator permissionClaims,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(
                    ResolveUserId(httpContext.User),
                    permissionClaims.ResolvePermissions(httpContext.User),
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/identity/open-access-clients/{result.Value!.Client.Id:D}",
                result.Value);
        })
        .WithName("identityCreateOpenAccessClient")
        .Produces<CreateOpenAccessClientResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Create);

        group.MapPut("/{clientId:guid}", async (
            Guid clientId,
            UpdateOpenAccessClientRequest request,
            OpenAccessClientManagementService service,
            PermissionClaimEvaluator permissionClaims,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(
                    ResolveUserId(httpContext.User),
                    permissionClaims.ResolvePermissions(httpContext.User),
                    clientId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateOpenAccessClient")
        .Produces<OpenAccessClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Update);

        group.MapPost("/{clientId:guid}/disable", async (
            Guid clientId,
            OpenAccessClientManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(clientId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableOpenAccessClient")
        .Produces<OpenAccessClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Disable);

        group.MapPost("/{clientId:guid}/rotate", async (
            Guid clientId,
            OpenAccessClientManagementService service,
            PermissionClaimEvaluator permissionClaims,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RotateAsync(
                    ResolveUserId(httpContext.User),
                    permissionClaims.ResolvePermissions(httpContext.User),
                    clientId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRotateOpenAccessClient")
        .Produces<CreateOpenAccessClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOpenAccessClientPermissions.Rotate);
    }

    private static Guid ResolveUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var userId)
            ? userId
            : Guid.Empty;
}
