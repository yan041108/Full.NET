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
            .WithTags("Identity");

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
        .Produces<PagedResult<HostMenuResponse>>(StatusCodes.Status200OK)
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
        .Produces<IReadOnlyList<HostMenuResponse>>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Read);

        group.MapGet("/permission-options", (
            HostMenuPermissionOptionsQueryService queries) =>
        {
            return Results.Ok(queries.List());
        })
        .Produces<IReadOnlyList<HostMenuPermissionOptionResponse>>(StatusCodes.Status200OK)
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
        .Produces<HostNavigationCatalogSyncResponse>(StatusCodes.Status200OK)
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
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
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
        .Produces<HostMenuResponse>(StatusCodes.Status201Created)
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
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
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
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
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
        .Produces<HostMenuResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityMenuManagementPermissions.Update);
    }
}
