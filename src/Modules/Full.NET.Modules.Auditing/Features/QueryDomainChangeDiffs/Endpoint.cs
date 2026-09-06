using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryDomainChangeDiffs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/auditing/domain-change-diffs", async (
            string? traceId,
            DomainAuditChangeDiffQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.QueryByTraceIdAsync(traceId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingQueryDomainChangeDiffs")
        .WithTags("AuditingDomainChangeDiffs")
        .Produces<DomainChangeDiffQueryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(DomainChangeDiffPermissions.Read));
    }
}
