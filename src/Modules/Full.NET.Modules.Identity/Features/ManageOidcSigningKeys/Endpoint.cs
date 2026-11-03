using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOidcClients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>OidcSigningKeys管理 API 路由注册。</summary>
namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/oidc-signing-keys")
            .WithTags("IdentityOidcSigningKeys");

        group.MapPost("/{keyId}/publish", async (string keyId, OidcSigningKeyManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.PublishAsync(keyId, actor, token).ConfigureAwait(false), context);
        }).WithName("identityPublishOidcSigningKey").Produces<OidcSigningKeyListResponse>()
            .RequireFullNetPermission(IdentityOidcSigningKeyPermissions.Publish);
        group.MapPost("/{keyId}/retire", async (string keyId, OidcSigningKeyManagementService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(context, out var actor)) return Results.Unauthorized();
            return mapper.Map(await service.RetireAsync(keyId, actor, token).ConfigureAwait(false), context);
        }).WithName("identityRetireOidcSigningKey").Produces<OidcSigningKeyListResponse>()
            .RequireFullNetPermission(IdentityOidcSigningKeyPermissions.Retire);

        group.MapGet("/", async (
            OidcSigningKeyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListOidcSigningKeys")
        .Produces<OidcSigningKeyListResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOidcSigningKeyPermissions.Read);

        group.MapPost("/{keyId}/activate", async (
            string keyId,
            OidcSigningKeyManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OidcManagementEndpointSupport.TryResolveActor(httpContext, out var actor))
            {
                return Results.Unauthorized();
            }

            var result = await service.ActivateAsync(keyId, actor, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityActivateOidcSigningKey")
        .Produces<OidcSigningKeyListResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityOidcSigningKeyPermissions.Activate);
    }
}
