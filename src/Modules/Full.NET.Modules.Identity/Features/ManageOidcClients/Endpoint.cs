using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageOidcClients;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/oidc-clients")
            .WithTags("IdentityOidcClients");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? clientIdContains,
            OidcClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    clientIdContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListOidcClients")
        .Produces<PagedResult<OidcClientResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Read);

        group.MapGet("/{clientId:guid}", async (
            Guid clientId,
            OidcClientQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(clientId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetOidcClient")
        .Produces<OidcClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Read);

        group.MapPost("/", async (
            CreateOidcClientRequest request,
            OidcClientManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(httpContext, out var actor))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(request, actor, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/identity/oidc-clients/{result.Value!.Client.Id:D}",
                result.Value);
        })
        .WithName("identityCreateOidcClient")
        .Produces<CreateOidcClientResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Create);

        group.MapPut("/{clientId:guid}", async (
            Guid clientId,
            UpdateOidcClientRequest request,
            OidcClientManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(httpContext, out var actor))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(clientId, request, actor, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateOidcClient")
        .Produces<OidcClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Update);

        group.MapPost("/{clientId:guid}/disable", async (
            Guid clientId,
            OidcClientManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(httpContext, out var actor))
            {
                return Results.Unauthorized();
            }

            var result = await service.DisableAsync(clientId, actor, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableOidcClient")
        .Produces<OidcClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Disable);

        group.MapPost("/{clientId:guid}/rotate", async (
            Guid clientId,
            OidcClientManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(httpContext, out var actor))
            {
                return Results.Unauthorized();
            }

            var result = await service.RotateAsync(clientId, actor, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRotateOidcClientSecret")
        .Produces<RotateOidcClientSecretResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOidcClientPermissions.Rotate);
    }
}