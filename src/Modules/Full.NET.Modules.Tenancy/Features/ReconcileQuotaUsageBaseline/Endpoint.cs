using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>ReconcileQuotaUsageBaseline 特性 HTTP 端点注册。</summary>
namespace Full.NET.Modules.Tenancy.Features.ReconcileQuotaUsageBaseline;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/tenancy/quota/usage-baseline",
                async (
                    ReconcileTenantQuotaUsageBaselineRequest request,
                    TenantQuotaUsageBaselineService service,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.ReconcileAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("TenancyTenantQuota")
            .WithName("tenancyReconcileQuotaUsageBaseline")
            .Produces<ReconcileTenantQuotaUsageBaselineResponse>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyTenantQuotaPermissions.ReconcileUsageBaseline));
    }
}
