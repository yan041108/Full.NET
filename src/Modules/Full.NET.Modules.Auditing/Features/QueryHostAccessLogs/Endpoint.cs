using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auditing/access-logs")
            .WithTags("AuditingHostAccessLogs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? httpMethod,
            int? statusCode,
            string? pathContains,
            HostAccessLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    fromUtc,
                    toUtc,
                    httpMethod,
                    statusCode,
                    pathContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingListHostAccessLogs")
        .Produces<PagedResult<AccessLogResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AccessLogPermissions.Read));

        group.MapGet("/cursor", async (
            int? limit,
            string? cursor,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? httpMethod,
            int? statusCode,
            string? pathContains,
            HostAccessLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListCursorAsync(
                    limit ?? 20,
                    cursor,
                    fromUtc,
                    toUtc,
                    httpMethod,
                    statusCode,
                    pathContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingListHostAccessLogsByCursor")
        .Produces<AccessLogCursorPageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AccessLogPermissions.Read));

        group.MapGet("/{accessLogId:guid}", async (
            Guid accessLogId,
            HostAccessLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(accessLogId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<AccessLogResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AccessLogPermissions.Read));
    }
}
