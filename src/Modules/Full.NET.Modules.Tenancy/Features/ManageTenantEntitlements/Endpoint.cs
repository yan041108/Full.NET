using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.MapGroup("/api/v1/tenancy/entitlements")
            .WithTags("TenancyTenantEntitlements");

        catalog.MapGet("/catalog", async (
            TenantEntitlementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListCatalogAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyListEntitlementCatalog")
        .Produces<IReadOnlyList<TenantEntitlementCatalogResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.Read));

        catalog.MapPost("/catalog", async (
            CreateTenantEntitlementCatalogRequest request,
            TenantEntitlementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateCatalogAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyCreateEntitlementCatalog")
        .Produces<TenantEntitlementCatalogResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.ManageCatalog));

        catalog.MapPost("/backfill", async (
            TenantEntitlementBackfillRequest request,
            TenantEntitlementBackfillService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReconcileAsync(request.DryRun, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyReconcileEntitlementBackfill")
        .Produces<TenantEntitlementBackfillResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.ReconcileBackfill));

        var tenants = endpoints.MapGroup("/api/v1/tenancy/tenants")
            .WithTags("TenancyTenantEntitlements");

        tenants.MapGet("/{tenantId:guid}/entitlements", async (
            Guid tenantId,
            TenantEntitlementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListBindingsAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyListTenantEntitlements")
        .Produces<IReadOnlyList<TenantEntitlementBindingResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.Read));

        tenants.MapPost("/{tenantId:guid}/entitlements", async (
            Guid tenantId,
            CreateTenantEntitlementBindingRequest request,
            TenantEntitlementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateBindingAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyCreateTenantEntitlementBinding")
        .Produces<TenantEntitlementBindingResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.ManageBindings));

        endpoints.MapGet("/api/v1/tenancy/settings/entitlement-enforcement", async (
            TenantEntitlementQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetEnforcementPhaseAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithTags("TenancyTenantEntitlements")
        .WithName("tenancyGetEntitlementEnforcementPhase")
        .Produces<TenantEntitlementEnforcementResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.ManageEnforcement));

        endpoints.MapPut("/api/v1/tenancy/settings/entitlement-enforcement", async (
            UpdateTenantEntitlementEnforcementRequest request,
            TenantEntitlementManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateEnforcementPhaseAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithTags("TenancyTenantEntitlements")
        .WithName("tenancyUpdateEntitlementEnforcementPhase")
        .Produces<TenantEntitlementEnforcementResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantEntitlementPermissions.ManageEnforcement));
    }
}
