using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/user-units")
            .WithTags("Organization");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? userId,
            Guid? unitId,
            TenantUserUnitQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    unitId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(OrganizationUserUnitManagementPermissions.Read);

        group.MapPost("/", async (
            CreateOrganizationUserUnitRequest request,
            TenantUserUnitManagementService service,
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
                $"/api/v1/organization/user-units/{result.Value!.Id:D}",
                result.Value);
        })
        .RequireFullNetPermission(OrganizationUserUnitManagementPermissions.Write);

        group.MapPut("/{assignmentId:guid}", async (
            Guid assignmentId,
            UpdateOrganizationUserUnitRequest request,
            TenantUserUnitManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(assignmentId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(OrganizationUserUnitManagementPermissions.Write);

        group.MapPost("/{assignmentId:guid}/disable", async (
            Guid assignmentId,
            TenantUserUnitManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(assignmentId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(OrganizationUserUnitManagementPermissions.Write);
    }
}
