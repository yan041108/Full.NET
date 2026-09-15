using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/oidc-signing-keys")
            .WithTags("IdentityOidcSigningKeys");

        group.MapGet("/", (
            OidcSigningKeyQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = queries.List();
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListOidcSigningKeys")
        .Produces<OidcSigningKeyListResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityOidcSigningKeyPermissions.Read);
    }
}