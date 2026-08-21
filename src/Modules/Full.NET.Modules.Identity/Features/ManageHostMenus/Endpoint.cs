using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/menus")
            .WithTags("IdentityHostMenus");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostMenuQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListHostMenus")
        .Produces<PagedResult<HostMenuResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Read);

        group.MapGet("/all", async (
            HostMenuQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAllAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListAllHostMenus")
        .Produces<IReadOnlyList<HostMenuResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Read);

        group.MapGet("/permission-options", (
            HostMenuPermissionOptionsQueryService queries) =>
        {
            return Results.Ok(queries.List());
        })
        .WithName("identityListHostMenuPermissionOptions")
        .Produces<IReadOnlyList<HostMenuPermissionOptionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Read);

        group.MapPost("/sync-catalog", async (
            HostNavigationCatalogSyncService catalogSync,
            CancellationToken cancellationToken) =>
        {
            var (created, skipped, reparented) = await catalogSync
                .SyncMissingCatalogEntriesAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new HostNavigationCatalogSyncResponse(created, skipped, reparented));
        })
        .WithName("identitySyncHostMenuCatalog")
        .Produces<HostNavigationCatalogSyncResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Update);

        group.MapGet("/{menuId:guid}", async (
            Guid menuId,
            HostMenuQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(menuId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetHostMenu")
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Read);

        group.MapPost("/", async (
            CreateHostMenuRequest request,
            HostMenuManagementService service,
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
                $"/api/v1/identity/menus/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("identityCreateHostMenu")
        .Produces<HostMenuResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Create);

        group.MapPut("/{menuId:guid}", async (
            Guid menuId,
            UpdateHostMenuRequest request,
            HostMenuManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(menuId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateHostMenu")
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Update);

        group.MapPost("/{menuId:guid}/disable", async (
            Guid menuId,
            HostMenuManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(menuId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableHostMenu")
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Disable);

        group.MapPost("/{menuId:guid}/enable", async (
            Guid menuId,
            HostMenuManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.EnableAsync(menuId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityEnableHostMenu")
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Update);
    }
}
