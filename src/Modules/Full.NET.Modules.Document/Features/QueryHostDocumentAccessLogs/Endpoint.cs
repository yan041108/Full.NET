using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Document.Features.QueryHostDocumentAccessLogs;

/// <summary>Host 文档访问日志 HTTP 端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document/host/access-logs")
            .WithTags("DocumentHostAccessLogs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? documentItemId,
            string? accessTypeKey,
            string? sourceKey,
            HostDocumentAccessLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    documentItemId,
                    accessTypeKey,
                    sourceKey,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("documentHostListDocumentAccessLogs")
        .Produces<PagedResult<HostDocumentAccessLogResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentAccessLogPermissions.Read));
    }
}
