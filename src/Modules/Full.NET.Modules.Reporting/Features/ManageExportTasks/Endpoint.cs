using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>报表导出任务 HTTP 端点。</summary>
internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>注册导出任务 API。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reporting/export-tasks")
            .WithTags("ReportingExportTasks");

        group.MapPost("/", async (
            CreateReportingExportTaskRequest request,
            ReportingExportTaskManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service
                .CreateAsync(request, userId, httpContext.User, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/reporting/export-tasks/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("reportingCreateExportTask")
        .Produces<ReportingExportTaskDetailResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExportTaskPermissions.Create));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? definitionId,
            ReportingExportTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(page ?? 1, pageSize ?? 20, definitionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingListExportTasks")
        .Produces<PagedResult<ReportingExportTaskResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExportTaskPermissions.Read));

        group.MapGet("/{taskId:guid}", async (
            Guid taskId,
            ReportingExportTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("reportingGetExportTask")
        .Produces<ReportingExportTaskDetailResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExportTaskPermissions.Read));

        group.MapGet("/{taskId:guid}/download", async (
            Guid taskId,
            ReportingExportTaskManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.OpenDownloadAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var content = result.Value!;
            return Results.File(
                content.Content,
                content.ContentType ?? WorkbookContentType,
                content.OriginalFileName ?? "reporting-export.xlsx");
        })
        .WithName("reportingDownloadExportTask")
        .Produces<Stream>(StatusCodes.Status200OK, WorkbookContentType)
        .RequireAuthorization(FullNetPermissionPolicies.For(ReportingExportTaskPermissions.Download));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
