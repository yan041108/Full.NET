using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobHealth;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/jobs/host-health",
                async (
                    HostJobHealthQueryService queries,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await queries.GetAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("JobsHostJobHealth")
            .WithName("jobsGetHostJobHealth")
            .Produces<HostJobHealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(
                FullNetPermissionPolicies.For(HostJobPermissions.HealthRead));
    }
}
