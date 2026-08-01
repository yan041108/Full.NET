using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.QueryHostModuleCatalog;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/modules")
            .WithTags("Identity");

        group.MapGet("/", async (
            HostModuleCatalogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<IReadOnlyList<ModuleCatalogEntryResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ModuleCatalogPermissions.Read));

        group.MapGet("/{moduleKey}", async (
            string moduleKey,
            HostModuleCatalogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByKeyAsync(moduleKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<ModuleCatalogEntryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(ModuleCatalogPermissions.Read));
    }
}
