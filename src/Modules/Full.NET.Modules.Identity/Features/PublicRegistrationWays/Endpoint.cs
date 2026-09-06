using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.PublicRegistrationWays;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/identity/public/registration-ways", async (
            Guid tenantId,
            PublicRegistrationWayQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListEnabledAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithTags("IdentityPublicRegistration")
        .WithName("identityListPublicRegistrationWays")
        .Produces<IReadOnlyList<PublicRegistrationWayResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .AllowAnonymous();
    }
}
