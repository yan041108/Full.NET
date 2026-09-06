using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务创建、查询与执行 HTTP 端点。</summary>
internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/import-export/tasks")
            .WithTags("ImportExportTasks");

        group.MapPost("/", async (
            [FromForm] string schemaKey,
            [FromForm] string worksheetKey,
            [FromForm] IFormFile file,
            ImportExportTaskManagementService service,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(schemaKey) || string.IsNullOrWhiteSpace(worksheetKey))
            {
                return mapper.Map(
                    Result<ImportExportTaskDetailResponse>.Failure(new Error(
                        ImportExportErrorCodes.FileInvalid,
                        "Schema key and worksheet key are required.",
                        ErrorType.Validation)),
                    httpContext);
            }

            await using var stream = file.OpenReadStream();
            var previewContext = BuildPreviewContext(userId, principal);
            var result = await service
                .CreateAsync(
                    schemaKey.Trim(),
                    worksheetKey.Trim(),
                    file.FileName,
                    stream,
                    file.Length,
                    userId,
                    previewContext,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/import-export/tasks/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("importExportCreateImportTask")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportExportTaskDetailResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .DisableAntiforgery()
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksCreate));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? schemaKey,
            ImportExportTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(page ?? 1, pageSize ?? 20, schemaKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportListImportTasks")
        .Produces<PagedResult<ImportExportTaskResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksRead));

        group.MapGet("/{taskId:guid}", async (
            Guid taskId,
            ImportExportTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportGetImportTask")
        .Produces<ImportExportTaskDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksRead));

        group.MapPost("/{taskId:guid}/execute", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await executionService
                .QueueExecuteAsync(
                    taskId,
                    BuildPreviewContext(userId, principal),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportExecuteImportTask")
        .Produces<ImportExportTaskDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapPost("/{taskId:guid}/resume", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await executionService
                .ResumeAsync(
                    taskId,
                    BuildPreviewContext(userId, principal),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportResumeImportTask")
        .Produces<ImportExportTaskDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapPost("/{taskId:guid}/retry", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await executionService
                .RetryAsync(
                    taskId,
                    BuildPreviewContext(userId, principal),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportRetryImportTask")
        .Produces<ImportExportTaskDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapGet("/{taskId:guid}/error-receipt", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await executionService
                .OpenErrorReceiptAsync(taskId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var content = result.Value!;
            return Results.File(
                content.Content,
                content.ContentType ?? WorkbookContentType,
                content.OriginalFileName ?? "import-task-errors.xlsx");
        })
        .WithName("importExportDownloadImportTaskErrorReceipt")
        .Produces<Stream>(StatusCodes.Status200OK, WorkbookContentType)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static StaticImportPreviewContext BuildPreviewContext(
        Guid requestedByUserId,
        ClaimsPrincipal principal)
    {
        var capabilityFlags = principal
            .FindAll(FullNetIdentityClaimTypes.Permission)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(permission => permission, _ => true, StringComparer.Ordinal);
        return new StaticImportPreviewContext(requestedByUserId, capabilityFlags);
    }
}
