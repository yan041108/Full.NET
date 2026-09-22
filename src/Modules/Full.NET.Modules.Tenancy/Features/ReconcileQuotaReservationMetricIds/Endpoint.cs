using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ReconcileQuotaReservationMetricIds;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/tenancy/quota/reservations/reconcile-metric-ids",
                async (
                    ReconcileTenantQuotaMetricIdsRequest request,
                    TenantQuotaMetricIdReconciliationService service,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.ReconcileAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("TenancyTenantQuota")
            .WithName("tenancyReconcileQuotaReservationMetricIds")
            .Produces<ReconcileTenantQuotaMetricIdsResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyTenantQuotaPermissions.ReconcileMetricIds));
    }
}
