using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenants/{tenantId:guid}/quota")
            .WithTags("TenancyTenantQuota");

        group.MapPost("/reserve", async (
            Guid tenantId,
            ReserveTenantQuotaRequest request,
            TenantQuotaReservationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReserveAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyReserveTenantQuota")
        .Produces<ReserveTenantQuotaResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantQuotaPermissions.Reserve));

        group.MapPost("/confirm", async (
            Guid tenantId,
            ConfirmTenantQuotaRequest request,
            TenantQuotaReservationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ConfirmAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyConfirmTenantQuota")
        .Produces<TenantQuotaMetricResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantQuotaPermissions.Reserve));

        group.MapPost("/release", async (
            Guid tenantId,
            ReleaseTenantQuotaRequest request,
            TenantQuotaReservationService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReleaseAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyReleaseTenantQuota")
        .Produces<TenantQuotaMetricResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantQuotaPermissions.Reserve));
    }
}
