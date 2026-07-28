using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auditing/operation-logs")
            .WithTags("Auditing");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? httpMethod,
            bool? succeeded,
            string? pathContains,
            HostOperationLogQueryService queries,
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
                    succeeded,
                    pathContains,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<PagedResult<OperationLogResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(FullNetPermissionPolicies.For(OperationLogPermissions.Read));

        group.MapGet("/{operationLogId:guid}", async (
            Guid operationLogId,
            HostOperationLogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(operationLogId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .Produces<OperationLogResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(OperationLogPermissions.Read));
    }
}
