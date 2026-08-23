using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageHostDictTypes;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/settings/dict-types")
            .WithTags("SettingsHostDictTypes");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostDictTypeQueryService queries,
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
        .WithName("settingsListHostDictTypes")
        .Produces<PagedResult<DictTypeResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));

        group.MapGet("/{dictTypeId:guid}", async (
            Guid dictTypeId,
            HostDictTypeQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DictTypeResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));

        group.MapPost("/", async (
            CreateDictTypeRequest request,
            HostDictTypeManagementService service,
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
                $"/api/v1/settings/dict-types/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("settingsCreateHostDictType")
        .Produces<DictTypeResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Create));

        group.MapPut("/{dictTypeId:guid}", async (
            Guid dictTypeId,
            UpdateDictTypeRequest request,
            HostDictTypeManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(dictTypeId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsUpdateHostDictType")
        .Produces<DictTypeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Update));

        group.MapPost("/{dictTypeId:guid}/disable", async (
            Guid dictTypeId,
            HostDictTypeManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsDisableHostDictType")
        .Produces<DictTypeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Disable));

        // 硬删除已禁用的字典类型，对应 Admin.NET DeleteDict；前置校验失败返回 ProblemDetails。
        group.MapPost("/{dictTypeId:guid}/delete", async (
            Guid dictTypeId,
            DeleteDictTypeRequest request,
            HostDictTypeManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(dictTypeId, request.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .WithName("settingsDeleteHostDictType")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Delete));

        // 全量字典类型列表（不分页），供下拉与全量消费场景使用。
        group.MapGet("/list", async (
            HostDictTypeQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAllAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsListAllHostDictTypes")
        .Produces<IReadOnlyList<DictTypeResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));

        // 按字典类型编码查询启用字典项，对应 Admin.NET dataList by code，供业务模块高频消费。
        group.MapGet("/by-code/{code}/items", async (
            string code,
            Features.ManageHostDictItems.HostDictItemQueryService itemQueries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await itemQueries.ListByTypeCodeAsync(code, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("settingsListHostDictItemsByTypeCode")
        .Produces<IReadOnlyList<DictItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            DictTypeManagementPermissions.Read));
    }
}
