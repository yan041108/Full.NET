using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.HostUserManagementReference;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/host-user-management")
            .WithTags("OrganizationHostUserManagement");

        group.MapGet("/reference", async (
            Guid tenantId,
            ClaimsPrincipal principal,
            HostUserManagementReferenceService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            if (!TryResolveReferenceAccess(
                    principal,
                    out var canAccessUserUnits,
                    out var canAccessUserPositions))
            {
                return Results.Forbid();
            }

            var result = await service.GetReferenceAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            // 当前聚合接口同时承载机构与职位参考数据，因此在路由层通过 Host 用户目录读取能力进入，
            // 再在处理器内部按精确组织权限做失败关闭与结果投影，避免无权限一侧的数据泄露。
            var value = result.Value!;
            return Results.Ok(new HostUserManagementOrganizationReferenceResponse(
                canAccessUserUnits ? value.Units : [],
                canAccessUserPositions ? value.Positions : [],
                canAccessUserUnits ? value.UserUnits : [],
                canAccessUserPositions ? value.UserPositions : []));
        })
        .WithName("organizationGetHostUserManagementReference")
        .Produces<HostUserManagementOrganizationReferenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Read));

        group.MapPost("/user-units", async (
            Guid tenantId,
            CreateOrganizationUserUnitRequest request,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserUnitManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.CreateAsync(request, cancellationToken)).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationCreateHostUserManagementUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));

        group.MapPut("/user-units/{assignmentId:guid}", async (
            Guid tenantId,
            Guid assignmentId,
            UpdateOrganizationUserUnitRequest request,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserUnitManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.UpdateAsync(assignmentId, request, cancellationToken))
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationUpdateHostUserManagementUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));

        group.MapPost("/user-units/{assignmentId:guid}/disable", async (
            Guid tenantId,
            Guid assignmentId,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserUnitManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.DisableAsync(assignmentId, cancellationToken))
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationDisableHostUserManagementUserUnit")
        .Produces<OrganizationUserUnitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));

        group.MapPost("/user-positions", async (
            Guid tenantId,
            CreateOrganizationUserPositionRequest request,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserPositionManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.CreateAsync(request, cancellationToken)).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationCreateHostUserManagementUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));

        group.MapPut("/user-positions/{assignmentId:guid}", async (
            Guid tenantId,
            Guid assignmentId,
            UpdateOrganizationUserPositionRequest request,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserPositionManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.UpdateAsync(assignmentId, request, cancellationToken))
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationUpdateHostUserManagementUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));

        group.MapPost("/user-positions/{assignmentId:guid}/disable", async (
            Guid tenantId,
            Guid assignmentId,
            ClaimsPrincipal principal,
            ICurrentTenantContextWriter currentTenant,
            HostUserManagementReferenceService tenantResolver,
            TenantUserPositionManagementService assignments,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveHostActor(principal, out _, out _))
            {
                return Results.Forbid();
            }

            var tenantResult = await tenantResolver.ResolveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!tenantResult.IsSuccess)
            {
                return mapper.Map(tenantResult, httpContext);
            }

            var tenant = tenantResult.Value!;
            var result = await HostUserManagementTenantScope.RunAsync(
                currentTenant,
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                () => assignments.DisableAsync(assignmentId, cancellationToken))
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationDisableHostUserManagementUserPosition")
        .Produces<OrganizationUserPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            IdentityUserManagementPermissions.Update));
    }

    private static bool TryResolveHostActor(
        ClaimsPrincipal principal,
        out Guid userId,
        out bool isSuperAdministrator)
    {
        if (!OrganizationActorContext.TryResolve(
                principal,
                out userId,
                out isSuperAdministrator))
        {
            return false;
        }

        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        return string.Equals(actorScope, "host", StringComparison.Ordinal);
    }

    private static bool TryResolveReferenceAccess(
        ClaimsPrincipal principal,
        out bool canAccessUserUnits,
        out bool canAccessUserPositions)
    {
        if (OrganizationActorContext.TryResolve(principal, out _, out var isSuperAdministrator)
            && isSuperAdministrator)
        {
            // 超级管理员令牌不携带 Permission Claim，Host 用户目录参考数据需按全量投影。
            canAccessUserUnits = true;
            canAccessUserPositions = true;
            return true;
        }

        canAccessUserUnits = HasAnyPermission(
            principal,
            IdentityUserManagementPermissions.Read,
            IdentityUserManagementPermissions.Update,
            OrganizationUserUnitManagementPermissions.Read,
            OrganizationUserUnitManagementPermissions.Create,
            OrganizationUserUnitManagementPermissions.Update,
            OrganizationUserUnitManagementPermissions.Disable);
        canAccessUserPositions = HasAnyPermission(
            principal,
            IdentityUserManagementPermissions.Read,
            IdentityUserManagementPermissions.Update,
            OrganizationUserPositionManagementPermissions.Read,
            OrganizationUserPositionManagementPermissions.Create,
            OrganizationUserPositionManagementPermissions.Update,
            OrganizationUserPositionManagementPermissions.Disable);
        return canAccessUserUnits || canAccessUserPositions;
    }

    private static bool HasAnyPermission(
        ClaimsPrincipal principal,
        params string[] permissionCodes) =>
        permissionCodes.Any(permissionCode =>
            principal.FindAll(FullNetIdentityClaimTypes.Permission)
                .Any(claim => string.Equals(
                    claim.Value,
                    permissionCode,
                    StringComparison.Ordinal)));
}
