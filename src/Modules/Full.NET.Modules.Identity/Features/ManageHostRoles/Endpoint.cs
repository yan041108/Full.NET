using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/roles")
            .WithTags("IdentityHostRoles");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostRoleQueryService queries,
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
        .WithName("identityListHostRoles")
        .Produces<PagedResult<HostRoleResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Read);

        group.MapGet("/{roleId:guid}", async (
            Guid roleId,
            HostRoleQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(roleId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetHostRole")
        .Produces<HostRoleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Read);

        group.MapPost("/", async (
            CreateHostRoleRequest request,
            HostRoleManagementService service,
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
                $"/api/v1/identity/roles/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("identityCreateHostRole")
        .Produces<HostRoleResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Create);

        group.MapPut("/{roleId:guid}", async (
            Guid roleId,
            UpdateHostRoleRequest request,
            HostRoleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(roleId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateHostRole")
        .Produces<HostRoleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Update);

        group.MapPut("/{roleId:guid}/permissions", async (
            Guid roleId,
            ReplaceHostRolePermissionsRequest request,
            HostRoleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReplacePermissionsAsync(
                    roleId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityReplaceHostRolePermissions")
        .Produces<HostRoleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.AssignPermissions);

        group.MapPost("/{roleId:guid}/disable", async (
            Guid roleId,
            HostRoleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(roleId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableHostRole")
        .Produces<HostRoleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Disable);

        group.MapGet("/{roleId:guid}/data-scope", async (
            Guid roleId,
            HostRoleDataScopeService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(roleId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetHostRoleDataScope")
        .Produces<HostRoleDataScopeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.Read);

        group.MapPut("/{roleId:guid}/data-scope", async (
            Guid roleId,
            UpdateHostRoleDataScopeRequest request,
            HostRoleDataScopeService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(roleId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateHostRoleDataScope")
        .Produces<HostRoleDataScopeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityRoleManagementPermissions.AssignDataScope);
    }
}
