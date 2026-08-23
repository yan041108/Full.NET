using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageHostDictItems;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var typeGroup = endpoints.MapGroup("/api/v1/settings/dict-types/{dictTypeId:guid}/items")
            .WithTags("SettingsHostDictTypes");

        typeGroup.MapGet("/", async (
            Guid dictTypeId,
            int? page,
            int? pageSize,
            HostDictItemQueryService queries,
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
        .WithName("settingsListHostDictItems")
        .Produces<PagedResult<DictItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));

        typeGroup.MapPost("/", async (
            Guid dictTypeId,
            CreateDictItemRequest request,
            HostDictItemManagementService service,
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
                $"/api/v1/settings/dict-items/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("settingsCreateHostDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Create));

        var itemGroup = endpoints.MapGroup("/api/v1/settings/dict-items")
            .WithTags("SettingsHostDictTypes");

        itemGroup.MapGet("/{dictItemId:guid}", async (
            Guid dictItemId,
            HostDictItemQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsGetHostDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));

        itemGroup.MapPut("/{dictItemId:guid}", async (
            Guid dictItemId,
            UpdateDictItemRequest request,
            HostDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(dictItemId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsUpdateHostDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Update));

        itemGroup.MapPost("/{dictItemId:guid}/disable", async (
            Guid dictItemId,
            HostDictItemManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsDisableHostDictItem")
        .Produces<DictItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Disable));

        // 硬删除已禁用的字典项，对应 Admin.NET DeleteDictItem；前置校验失败返回 ProblemDetails。
        itemGroup.MapPost("/{dictItemId:guid}/delete", async (
            Guid dictItemId,
            DeleteDictItemRequest request,
            HostDictItemManagementService service,
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
        .WithName("settingsDeleteHostDictItem")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Delete));
    }
}
