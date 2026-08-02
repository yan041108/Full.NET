using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageTenantDictTypes;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/settings/tenant-dict-types")
            .WithTags("Settings");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            TenantDictTypeQueryService queries,
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
        .Produces<PagedResult<DictTypeResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Read));

        group.MapGet("/{dictTypeId:guid}", async (
            Guid dictTypeId,
            TenantDictTypeQueryService queries,
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
            TenantDictTypeManagementPermissions.Read));

        group.MapPost("/", async (
            CreateDictTypeRequest request,
            TenantDictTypeManagementService service,
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
                $"/api/v1/settings/tenant-dict-types/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<DictTypeResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Create));

        group.MapPut("/{dictTypeId:guid}", async (
            Guid dictTypeId,
            UpdateDictTypeRequest request,
            TenantDictTypeManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(dictTypeId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DictTypeResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Update));

        group.MapPost("/{dictTypeId:guid}/disable", async (
            Guid dictTypeId,
            TenantDictTypeManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<DictTypeResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenantDictTypeManagementPermissions.Disable));
    }
}
