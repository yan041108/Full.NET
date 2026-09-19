using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantQuota;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenants/{tenantId:guid}/quota/metrics")
            .WithTags("TenancyTenantQuota");

        group.MapGet("/", async (
            Guid tenantId,
            TenantQuotaManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyListTenantQuotaMetrics")
        .Produces<IReadOnlyList<TenantQuotaMetricResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantQuotaPermissions.Read));

        group.MapPut("/", async (
            Guid tenantId,
            UpsertTenantQuotaMetricRequest request,
            TenantQuotaManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpsertAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyUpsertTenantQuotaMetric")
        .Produces<TenantQuotaMetricResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantQuotaPermissions.Manage));
    }
}
