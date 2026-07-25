using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenants")
            .WithTags("Tenancy");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostTenantQueryService queries,
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
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.HostTenantsRead));

        group.MapGet("/{tenantId:guid}", async (
            Guid tenantId,
            HostTenantQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.HostTenantsRead));

        group.MapPost("/", async (
            ProvisionTenantRequest request,
            ITenantProvisioningService provisioning,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await provisioning.ProvisionAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/tenancy/tenants/{result.Value!.Id:D}",
                result.Value);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.Write));

        group.MapPut("/{tenantId:guid}", async (
            Guid tenantId,
            UpdateHostTenantRequest request,
            HostTenantManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.Write));

        group.MapPost("/{tenantId:guid}/disable", async (
            Guid tenantId,
            HostTenantManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.Write));

        group.MapPost("/{tenantId:guid}/package", async (
            Guid tenantId,
            AssignHostTenantPackageRequest request,
            HostTenantManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AssignPackageAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.Write));
    }
}
