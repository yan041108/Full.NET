using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositionLevels;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/position-levels")
            .WithTags("Organization");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            TenantPositionLevelQueryService queries,
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
        .Produces<PagedResult<OrganizationPositionLevelResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionLevelManagementPermissions.Read));

        group.MapGet("/{positionLevelId:guid}", async (
            Guid positionLevelId,
            TenantPositionLevelQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(positionLevelId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationPositionLevelResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionLevelManagementPermissions.Read));

        group.MapPost("/", async (
            CreateOrganizationPositionLevelRequest request,
            TenantPositionLevelManagementService service,
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
                $"/api/v1/organization/position-levels/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<OrganizationPositionLevelResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionLevelManagementPermissions.Create));

        group.MapPut("/{positionLevelId:guid}", async (
            Guid positionLevelId,
            UpdateOrganizationPositionLevelRequest request,
            TenantPositionLevelManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(
                    positionLevelId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationPositionLevelResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionLevelManagementPermissions.Update));

        group.MapPost("/{positionLevelId:guid}/disable", async (
            Guid positionLevelId,
            TenantPositionLevelManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(positionLevelId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationPositionLevelResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionLevelManagementPermissions.Disable));
    }
}
