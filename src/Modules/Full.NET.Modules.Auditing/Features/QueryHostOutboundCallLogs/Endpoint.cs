using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryHostOutboundCallLogs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auditing/outbound-call-logs")
            .WithTags("AuditingHostOutboundCallLogs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? providerKey,
            bool? succeeded,
            string? operationContains,
            HostOutboundCallLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    fromUtc,
                    toUtc,
                    providerKey,
                    succeeded,
                    operationContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingListHostOutboundCallLogs")
        .Produces<PagedResult<OutboundCallLogResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(OutboundCallLogPermissions.Read));

        group.MapGet("/{outboundCallLogId:guid}", async (
            Guid outboundCallLogId,
            HostOutboundCallLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(outboundCallLogId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OutboundCallLogResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OutboundCallLogPermissions.Read));
    }
}
