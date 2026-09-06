using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/platform/host-dashboard-summary",
                async (
                    HostDashboardQueryService queries,
                    PermissionClaimEvaluator permissionClaims,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (!TryGetSubject(httpContext.User, out var actorUserId))
                    {
                        return Results.Unauthorized();
                    }

                    var result = await queries.GetSummaryAsync(
                            httpContext.User,
                            permissionClaims,
                            actorUserId,
                            cancellationToken)
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

    private static bool TryGetSubject(
        System.Security.Claims.ClaimsPrincipal principal,
        out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out userId)
            && userId != Guid.Empty;
    }
}
