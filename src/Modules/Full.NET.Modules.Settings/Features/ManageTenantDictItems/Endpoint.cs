using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageTenantDictItems;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var typeGroup = endpoints
            .MapGroup("/api/v1/settings/tenant-dict-types/{dictTypeId:guid}/items")
            .WithTags("SettingsTenantDictTypes");

        typeGroup.MapGet("/", async (
            Guid dictTypeId,
            int? page,
            int? pageSize,
            TenantDictItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListByTypeIdAsync(
                    dictTypeId,
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsListTenantDictItems")
        .Produces<PagedResult<DictItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Read));

        typeGroup.MapPost("/", async (
            Guid dictTypeId,
            CreateDictItemRequest request,
            TenantDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(dictTypeId, request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/settings/tenant-dict-items/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("settingsCreateTenantDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Create));

        var itemGroup = endpoints.MapGroup("/api/v1/settings/tenant-dict-items")
            .WithTags("SettingsTenantDictTypes");

        itemGroup.MapGet("/{dictItemId:guid}", async (
            Guid dictItemId,
            TenantDictItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsGetTenantDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Read));

        itemGroup.MapPut("/{dictItemId:guid}", async (
            Guid dictItemId,
            UpdateDictItemRequest request,
            TenantDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(dictItemId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsUpdateTenantDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Update));

        itemGroup.MapPost("/{dictItemId:guid}/disable", async (
            Guid dictItemId,
            TenantDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsDisableTenantDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Disable));

        itemGroup.MapPost("/{dictItemId:guid}/delete", async (
            Guid dictItemId,
            DeleteDictItemRequest request,
            TenantDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(dictItemId, request.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .WithName("settingsDeleteTenantDictItem")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Delete));
    }
}
