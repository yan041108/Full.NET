using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Files.Features.ManageStorageProviders;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/files/storage-providers")
            .WithTags("FilesStorageProviders");

        group.MapGet("/", (
            FileStorageProviderCatalogService catalog) =>
            Results.Ok(catalog.List()))
        .WithName("filesListStorageProviders")
        .Produces<IReadOnlyList<StorageProviderCatalogItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(StorageProviderPermissions.Read));

        group.MapPost("/{providerKey}/test", async (
            string providerKey,
            FileStorageProviderCatalogService catalog,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await catalog.TestConnectivityAsync(providerKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesTestStorageProviderConnectivity")
        .Produces<TestStorageProviderConnectivityResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(StorageProviderPermissions.Test));
    }
}
