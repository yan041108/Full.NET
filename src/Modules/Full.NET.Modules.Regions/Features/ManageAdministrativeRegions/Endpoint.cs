using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Regions.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Regions.Features.ManageAdministrativeRegions;

/// <summary>行政区域管理 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册行政区域管理路由组。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/regions/administrative-regions")
            .WithTags("RegionsAdministrativeRegions");

        group.MapGet("/children", async (
            Guid? parentId,
            AdministrativeRegionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListChildrenAsync(parentId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsListAdministrativeRegionChildren")
        .Produces<IReadOnlyList<AdministrativeRegionChildResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Read));

        group.MapGet("/tree", async (
            Guid? parentId,
            int? maxDepth,
            AdministrativeRegionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetTreeAsync(parentId, maxDepth ?? 3, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsGetAdministrativeRegionTree")
        .Produces<IReadOnlyList<AdministrativeRegionTreeNodeResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Read));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? parentId,
            string? name,
            string? code,
            int? level,
            AdministrativeRegionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    parentId,
                    name,
                    code,
                    level,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsListAdministrativeRegions")
        .Produces<PagedResult<AdministrativeRegionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Read));

        group.MapGet("/dataset-manifest/latest", async (
            string? datasetKey,
            AdministrativeRegionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetLatestManifestAsync(
                    datasetKey ?? "china.administrative",
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsGetLatestAdministrativeRegionDatasetManifest")
        .Produces<AdministrativeRegionDatasetManifestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Read));

        group.MapGet("/{regionId:guid}", async (
            Guid regionId,
            AdministrativeRegionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(regionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsGetAdministrativeRegion")
        .Produces<AdministrativeRegionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Read));

        group.MapPost("/", async (
            CreateAdministrativeRegionRequest request,
            AdministrativeRegionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/regions/administrative-regions/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("regionsCreateAdministrativeRegion")
        .Produces<AdministrativeRegionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Create));

        group.MapPut("/{regionId:guid}", async (
            Guid regionId,
            UpdateAdministrativeRegionRequest request,
            AdministrativeRegionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(regionId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsUpdateAdministrativeRegion")
        .Produces<AdministrativeRegionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Update));

        group.MapPost("/{regionId:guid}/delete", async (
            Guid regionId,
            DeleteAdministrativeRegionRequest request,
            AdministrativeRegionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(regionId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .WithName("regionsDeleteAdministrativeRegion")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Delete));

        group.MapPost("/import/preview", async (
            ImportAdministrativeRegionsRequest request,
            AdministrativeRegionImportService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.PreviewAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsPreviewAdministrativeRegionImport")
        .Produces<ImportAdministrativeRegionsPreviewResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Import));

        group.MapPost("/import/apply", async (
            ImportAdministrativeRegionsRequest request,
            AdministrativeRegionImportService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.ApplyAsync(request, userId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("regionsApplyAdministrativeRegionImport")
        .Produces<ImportAdministrativeRegionsApplyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(RegionsPermissions.Import));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
