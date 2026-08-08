using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageHostConfigEntries;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/settings/config-entries")
            .WithTags("Settings");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostConfigEntryQueryService queries,
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
        .Produces<PagedResult<ConfigEntryResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Read));

        group.MapGet("/by-key/{configKey}", async (
            string configKey,
            HostConfigEntryQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByKeyAsync(configKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<ConfigEntryResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Read));

        group.MapGet("/{configEntryId:guid}", async (
            Guid configEntryId,
            HostConfigEntryQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(configEntryId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<ConfigEntryResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Read));

        group.MapPost("/", async (
            CreateConfigEntryRequest request,
            HostConfigEntryManagementService service,
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
                $"/api/v1/settings/config-entries/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<ConfigEntryResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Create));

        group.MapPut("/{configEntryId:guid}", async (
            Guid configEntryId,
            UpdateConfigEntryRequest request,
            HostConfigEntryManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(configEntryId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<ConfigEntryResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Update));

        group.MapPost("/{configEntryId:guid}/disable", async (
            Guid configEntryId,
            HostConfigEntryManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(configEntryId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<ConfigEntryResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Disable));

        // 硬删除已禁用的配置项，对应 Admin.NET DeleteConfig；前置校验失败返回 ProblemDetails。
        group.MapPost("/{configEntryId:guid}/delete", async (
            Guid configEntryId,
            DeleteConfigEntryRequest request,
            HostConfigEntryManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(configEntryId, request.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Delete));

        // 批量硬删除已禁用的配置项，对应 Admin.NET batchDeleteConfig。
        group.MapPost("/batch-delete", async (
            BatchDeleteConfigEntriesRequest request,
            HostConfigEntryManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BatchDeleteAsync(request.Ids, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Delete));

        // 批量更新配置项值，对应 Admin.NET batchUpdateConfigValue。
        group.MapPost("/batch-update-values", async (
            BatchUpdateConfigValuesRequest request,
            HostConfigEntryManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BatchUpdateValuesAsync(request.Updates, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Update));

        // 全量配置项列表（不分页），供全量消费与导出场景使用。
        group.MapGet("/list", async (
            HostConfigEntryQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAllAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<IReadOnlyList<ConfigEntryResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Read));

        // 查询配置项分组去重列表，供分组下拉与按组筛选使用。
        group.MapGet("/groups", async (
            HostConfigEntryQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListGroupsAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ConfigEntryManagementPermissions.Read));
    }
}
