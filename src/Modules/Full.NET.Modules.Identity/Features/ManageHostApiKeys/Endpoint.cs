using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Full.NET.Modules.Identity.Features.ManageHostApiKeys;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/api-keys")
            .WithTags("IdentityHostApiKeys");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? userId,
            string? displayNameContains,
            HostApiKeyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    displayNameContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListHostApiKeys")
        .Produces<PagedResult<HostApiKeyResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityApiKeyManagementPermissions.Read);

        group.MapPost("/", async (
            CreateHostApiKeyRequest request,
            HostApiKeyManagementService service,
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
                $"/api/v1/identity/api-keys/{result.Value!.Key.Id:D}",
                result.Value);
        })
        .WithName("identityCreateHostApiKey")
        .Produces<CreateHostApiKeyResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityApiKeyManagementPermissions.Create);

        group.MapPost("/{apiKeyId:guid}/disable", async (
            Guid apiKeyId,
            HostApiKeyManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(apiKeyId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableHostApiKey")
        .Produces<HostApiKeyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityApiKeyManagementPermissions.Disable);

        group.MapPost("/{apiKeyId:guid}/rotate", async (
            Guid apiKeyId,
            HostApiKeyManagementService service,
            PermissionClaimEvaluator permissionClaims,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RotateAsync(
                    ResolveUserId(httpContext.User),
                    permissionClaims.ResolvePermissions(httpContext.User),
                    apiKeyId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRotateHostApiKey")
        .Produces<CreateHostApiKeyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityApiKeyManagementPermissions.Rotate);
    }

    private static Guid ResolveUserId(System.Security.Claims.ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var userId)
            ? userId
            : Guid.Empty;
}
