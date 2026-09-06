using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/observability/server-instances")
            .WithTags("ObservabilityServerMonitor");

        group.MapGet("/", (ServerMonitorService monitor) =>
                Results.Ok(monitor.ListInstances()))
            .WithName("observabilityListServerInstances")
            .Produces<IReadOnlyList<ServerInstanceCatalogEntry>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                ObservabilityServerPermissions.Read));

        group.MapGet("/{instanceKey}/runtime", async (
            string instanceKey,
            ServerMonitorService monitor,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await monitor.GetRuntimeAsync(instanceKey, cancellationToken)
                .ConfigureAwait(false);
            return snapshot is null
                ? NotFound(mapper, httpContext)
                : Results.Ok(snapshot);
        })
        .WithName("observabilityGetServerRuntime")
        .Produces<ServerRuntimeSnapshot>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityServerPermissions.Read));
    }

    private static IResult NotFound(
        IApiResultMapper mapper,
        HttpContext httpContext) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                ObservabilityAdminErrorCodes.ServerInstanceNotFound,
                "The server instance was not found or cannot be queried from the current process.",
                ErrorType.NotFound)),
            httpContext);
}
