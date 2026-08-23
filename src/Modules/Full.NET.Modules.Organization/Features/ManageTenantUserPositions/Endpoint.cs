using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ListAssignableHostUsers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/user-positions")
            .WithTags("OrganizationTenantUserPositions");

        group.MapGet("/assignable-users", async (
            int? page,
            int? pageSize,
            AssignableHostUserQueryService queries,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 100,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("organizationListAssignableTenantUserPositionUsers")
        .Produces<PagedResult<OrganizationAssignableUserResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserPositionManagementPermissions.Create));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? userId,
            Guid? positionId,
            TenantUserPositionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    positionId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationListTenantUserPositions")
        .Produces<PagedResult<OrganizationUserPositionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserPositionManagementPermissions.Read));

        group.MapPost("/", async (
            CreateOrganizationUserPositionRequest request,
            TenantUserPositionManagementService service,
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
                $"/api/v1/organization/user-positions/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("organizationCreateTenantUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserPositionManagementPermissions.Create));

        group.MapPut("/{assignmentId:guid}", async (
            Guid assignmentId,
            UpdateOrganizationUserPositionRequest request,
            TenantUserPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(assignmentId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationUpdateTenantUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserPositionManagementPermissions.Update));

        group.MapPost("/{assignmentId:guid}/disable", async (
            Guid assignmentId,
            TenantUserPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(assignmentId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationDisableTenantUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserPositionManagementPermissions.Disable));
    }
}
