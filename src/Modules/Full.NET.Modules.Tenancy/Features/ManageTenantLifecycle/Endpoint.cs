using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenants")
            .WithTags("TenancyTenantLifecycle");

        group.MapPost("/{tenantId:guid}/suspend", async (
            Guid tenantId,
            SuspendTenantRequest request,
            TenantLifecycleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SuspendAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancySuspendTenant")
        .Produces<TenantSummary>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantLifecyclePermissions.Suspend));

        group.MapPost("/{tenantId:guid}/reactivate", async (
            Guid tenantId,
            ReactivateTenantRequest request,
            TenantLifecycleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReactivateAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyReactivateTenant")
        .Produces<TenantSummary>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantLifecyclePermissions.Reactivate));

        group.MapPost("/{tenantId:guid}/close", async (
            Guid tenantId,
            CloseTenantRequest request,
            TenantLifecycleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CloseAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyCloseTenant")
        .Produces<TenantSummary>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantLifecyclePermissions.Close));

        group.MapPost("/{tenantId:guid}/transfer-ownership", async (
            Guid tenantId,
            TransferTenantOwnershipRequest request,
            TenantLifecycleManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TransferOwnershipAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyTransferTenantOwnership")
        .Produces<TenantSummary>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantLifecyclePermissions.TransferOwnership));
    }
}
