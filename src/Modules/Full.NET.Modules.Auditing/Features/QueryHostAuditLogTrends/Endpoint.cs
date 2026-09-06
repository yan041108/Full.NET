using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryHostAuditLogTrends;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        MapTrend(
            endpoints,
            "/api/v1/auditing/access-logs/trends",
            "auditingQueryHostAccessLogTrends",
            "AuditingHostAccessLogs",
            (service, fromUtc, toUtc, bucketMinutes, cancellationToken) =>
                service.QueryAccessTrendAsync(fromUtc, toUtc, bucketMinutes, cancellationToken));

        MapTrend(
            endpoints,
            "/api/v1/auditing/operation-logs/trends",
            "auditingQueryHostOperationLogTrends",
            "AuditingHostOperationLogs",
            (service, fromUtc, toUtc, bucketMinutes, cancellationToken) =>
                service.QueryOperationTrendAsync(fromUtc, toUtc, bucketMinutes, cancellationToken));

        MapTrend(
            endpoints,
            "/api/v1/auditing/exception-logs/trends",
            "auditingQueryHostExceptionLogTrends",
            "AuditingHostExceptionLogs",
            (service, fromUtc, toUtc, bucketMinutes, cancellationToken) =>
                service.QueryExceptionTrendAsync(fromUtc, toUtc, bucketMinutes, cancellationToken));
    }

    private static void MapTrend(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationName,
        string tag,
        Func<HostAuditLogTrendQueryService, DateTimeOffset?, DateTimeOffset?, int?, CancellationToken, Task<Result<AuditLogTrendResponse>>> query)
    {
        endpoints.MapGet(route, async (
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? bucketMinutes,
            HostAuditLogTrendQueryService trendQueries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await query(
                    trendQueries,
                    fromUtc,
                    toUtc,
                    bucketMinutes,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName(operationName)
        .WithTags(tag)
        .Produces<AuditLogTrendResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AuditLogTrendPermissions.Read));
    }
}
