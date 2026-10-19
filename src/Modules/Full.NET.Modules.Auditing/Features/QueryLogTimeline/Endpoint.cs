using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryLogTimeline;

/// <summary>提供带有明确覆盖范围的 Host 日志时间线。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/v1/auditing/timeline",
        async (DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? traceId, Guid? sessionId,
            Guid? userId, string? clientIpFingerprint, int? limit, LogTimelineQueryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if ((fromUtc.HasValue != toUtc.HasValue) || (fromUtc is null && string.IsNullOrEmpty(traceId)))
                return mapper.Map(Result<LogTimelineResponse>.Failure(new Error("auditing.timeline.query_invalid",
                    "A bounded time range is required without TraceId.", ErrorType.Validation)), context);
            var end = toUtc ?? DateTimeOffset.UtcNow;
            var query = new LogTimelineQuery((fromUtc ?? end.AddDays(-1)).ToUniversalTime(), end.ToUniversalTime(),
                traceId?.ToLowerInvariant(), sessionId, userId, clientIpFingerprint?.ToLowerInvariant(), limit ?? 100);
            return mapper.Map(await service.QueryAsync(query, context.User, token).ConfigureAwait(false), context);
        })
        .WithTags("AuditingLogTimeline")
        .WithName("auditingQueryLogTimeline")
        .Produces<LogTimelineResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(LogTimelinePermissions.Read));
}
