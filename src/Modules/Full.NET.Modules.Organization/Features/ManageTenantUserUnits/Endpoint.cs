using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ListAssignableHostUsers;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/user-units")
            .WithTags("OrganizationTenantUserUnits");

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
        .WithName("organizationListAssignableTenantUserUnitUsers")
        .Produces<PagedResult<OrganizationAssignableUserResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserUnitManagementPermissions.Create));

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            int? page,
            int? pageSize,
            Guid? userId,
            Guid? unitId,
            TenantUserUnitQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OrganizationActorContext.TryResolve(
                    principal,
                    out var actorUserId,
                    out var isSuperAdministrator))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ListAsync(
                    actorUserId,
                    isSuperAdministrator,
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    unitId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationListTenantUserUnits")
        .Produces<PagedResult<OrganizationUserUnitResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserUnitManagementPermissions.Read));

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
        .WithName("organizationCreateTenantUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserUnitManagementPermissions.Create));

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
        .WithName("organizationUpdateTenantUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserUnitManagementPermissions.Update));

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
        .WithName("organizationDisableTenantUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUserUnitManagementPermissions.Disable));
    }
}
