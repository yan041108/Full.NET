using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/platform/host-dashboard-summary",
                async (
                    HostDashboardQueryService queries,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await queries.GetSummaryAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("PlatformHostDashboard")
            .WithName("platformGetHostDashboardSummary")
            .Produces<HostDashboardSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(
                FullNetPermissionPolicies.For(IdentityAuthorizationContributor.DashboardRead));
    }
}
