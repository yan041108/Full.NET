using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantUnits;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/units")
            .WithTags("Organization");

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            int? page,
            int? pageSize,
            TenantUnitQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OrganizationActorContext.TryResolve(
                    principal,
                    out var userId,
                    out var isSuperAdministrator))
            {
                return Results.Unauthorized();
            }

            var result = await queries.ListAsync(
                    userId,
                    isSuperAdministrator,
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<OrganizationUnitResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUnitManagementPermissions.Read));

        group.MapGet("/{unitId:guid}", async (
            Guid unitId,
            ClaimsPrincipal principal,
            TenantUnitQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!OrganizationActorContext.TryResolve(
                    principal,
                    out var userId,
                    out var isSuperAdministrator))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetByIdForActorAsync(
                    unitId,
                    userId,
                    isSuperAdministrator,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationUnitResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUnitManagementPermissions.Read));

        group.MapPost("/", async (
            CreateOrganizationUnitRequest request,
            TenantUnitManagementService service,
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
                $"/api/v1/organization/units/{result.Value!.Id:D}",
                result.Value);
        })
        .Produces<OrganizationUnitResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUnitManagementPermissions.Write));

        group.MapPut("/{unitId:guid}", async (
            Guid unitId,
            UpdateOrganizationUnitRequest request,
            TenantUnitManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(unitId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationUnitResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUnitManagementPermissions.Write));

        group.MapPost("/{unitId:guid}/disable", async (
            Guid unitId,
            TenantUnitManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(unitId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OrganizationUnitResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationUnitManagementPermissions.Write));
    }
}
