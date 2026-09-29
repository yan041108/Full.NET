using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
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
            .WithTags("AuditingHostOperationLogs");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? httpMethod,
            bool? succeeded,
            string? pathContains,
            HostOperationLogQueryService queries,
            HttpOperationPayloadProjection payloadProjection,
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
            CapturePaginationForLog(result, payloadProjection, httpContext);
            CapturePaginationResultForLog(result, payloadProjection, httpContext);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingListHostOperationLogs")
        .Produces<PagedResult<OperationLogResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
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

        group.MapGet("/{operationLogId:guid}/details", async (
            Guid operationLogId,
            HostOperationLogDetailsQueryService details,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await details.GetByIdAsync(operationLogId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("auditingGetHostOperationLogDetails")
        .Produces<OperationLogDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(
            FullNetPermissionPolicies.For(OperationLogPermissions.Read),
            FullNetPermissionPolicies.For(OperationLogPermissions.ReadDetails));
    }

    internal static void CapturePaginationForLog(
        Result<PagedResult<OperationLogResponse>> result,
        HttpOperationPayloadProjection payloadProjection,
        HttpContext httpContext)
    {
        try
        {
            if (!result.IsSuccess || result.Value is not { } pageResult
                || !payloadProjection.TryBeginCapture(
                    httpContext,
                    HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.Pagination,
                    out var lease))
            {
                return;
            }

            using (lease)
            {
                // 只投影查询结果中的规范化数值；筛选文本和原始 Query 均不得进入普通日志。
                lease!.Capture(
                    () => new HttpPaginationLogProjection(pageResult.Page, pageResult.PageSize),
                    HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
            }
        }
        catch (Exception)
        {
            // 日志投影连同源生成元数据解析都只是旁路，失败不得替换已成功的查询响应。
        }
    }

    internal static void CapturePaginationResultForLog(
        Result<PagedResult<OperationLogResponse>> result,
        HttpOperationPayloadProjection payloadProjection,
        HttpContext httpContext)
    {
        try
        {
            if (!result.IsSuccess || result.Value is not { } pageResult
                || !payloadProjection.TryBeginCapture(
                    httpContext,
                    HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.PaginationResult,
                    out var lease))
            {
                return;
            }

            using (lease)
            {
                // 返回摘要只取分页数字，不接触列表对象内容或实际响应流。
                lease!.Capture(
                    () => new HttpPaginationResultLogProjection(
                        pageResult.Page,
                        pageResult.PageSize,
                        pageResult.Total,
                        pageResult.Items.Count),
                    HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection);
            }
        }
        catch (Exception)
        {
            // 返回日志摘要失败不影响业务响应。
        }
    }
}
