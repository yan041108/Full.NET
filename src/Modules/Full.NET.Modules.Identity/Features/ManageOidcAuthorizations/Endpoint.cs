using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/oidc-authorizations")
            .WithTags("IdentityOidcAuthorizations");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? applicationId,
            string? subject,
            string? status,
            string? clientIdContains,
            OidcAuthorizationQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    applicationId,
                    subject,
                    status,
                    clientIdContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListOidcAuthorizations")
        .Produces<PagedResult<OidcAuthorizationResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOidcAuthorizationPermissions.Read);

        group.MapGet("/{id:guid}", async (
            Guid id,
            OidcAuthorizationQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetOidcAuthorization")
        .Produces<OidcAuthorizationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOidcAuthorizationPermissions.Read);

        group.MapPost("/{id:guid}/revoke", async (
            Guid id,
            OidcAuthorizationManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RevokeAsync(id, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRevokeOidcAuthorization")
        .Produces<OidcAuthorizationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityOidcAuthorizationPermissions.Revoke);
    }
}