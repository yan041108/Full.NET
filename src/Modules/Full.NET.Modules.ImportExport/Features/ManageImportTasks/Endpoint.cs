using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Serialization;
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
            [FromForm] string? columnMapping,
            ImportExportTaskManagementService service,
            ClaimsPrincipal principal,
            ICurrentTenant currentTenant,
            IIdentityPermissionEvaluator permissions,
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

            var form = await httpContext.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            if (form.Any(pair => pair.Key is not ("schemaKey" or "worksheetKey" or "columnMapping") || pair.Value.Count != 1)
                || form.Files.Count != 1 || form.Files[0].Name != "file")
                return mapper.Map(Result<ImportExportTaskDetailResponse>.Failure(ImportColumnMappingSnapshots.Invalid()), httpContext);
            ImportColumnMappingRequest? mapping = null;
            if (form.ContainsKey("columnMapping"))
            {
                if (string.IsNullOrWhiteSpace(columnMapping) || columnMapping.Length > StaticImportHeaderMapping.MaximumConfigurationBytes
                    || Encoding.UTF8.GetByteCount(columnMapping) > StaticImportHeaderMapping.MaximumConfigurationBytes)
                    return mapper.Map(Result<ImportExportTaskDetailResponse>.Failure(ImportColumnMappingSnapshots.Invalid()), httpContext);
                try { mapping = JsonSerializer.Deserialize(columnMapping, ImportExportJsonSerializerContext.Default.ImportColumnMappingRequest); }
                catch (JsonException) { return mapper.Map(Result<ImportExportTaskDetailResponse>.Failure(ImportColumnMappingSnapshots.Invalid()), httpContext); }
                if (mapping is null) return mapper.Map(Result<ImportExportTaskDetailResponse>.Failure(ImportColumnMappingSnapshots.Invalid()), httpContext);
            }
            await using var stream = file.OpenReadStream();
            var previewContext = BuildPreviewContext(userId, principal, permissions, currentTenant.Id) with { ColumnMapping = mapping };
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

        // 本人查询不接受用户或租户参数；身份只来自已认证主体和可信上下文。
        group.MapGet("/mine", async (int? pageSize, string? statusKey, string? cursor,
            ImportExportTaskQueryService queries, IApiResultMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId) || userId == Guid.Empty)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            return mapper.Map(await queries.ListMineAsync(userId, pageSize ?? 20, statusKey, cursor, cancellationToken).ConfigureAwait(false), httpContext);
        }).WithName("importExportListMyImportTasks")
            .Produces<CursorPage<ImportExportTaskResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksRead));

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? schemaKey,
            string? statusKey,
            ImportExportTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries
                .ListAsync(page ?? 1, pageSize ?? 20, schemaKey, statusKey, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportListImportTasks")
        .Produces<PagedResult<ImportExportTaskResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
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
            ICurrentTenant currentTenant,
            IIdentityPermissionEvaluator permissions,
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
                    BuildPreviewContext(userId, principal, permissions, currentTenant.Id),
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
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapPost("/{taskId:guid}/resume", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ClaimsPrincipal principal,
            ICurrentTenant currentTenant,
            IIdentityPermissionEvaluator permissions,
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
                    BuildPreviewContext(userId, principal, permissions, currentTenant.Id),
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
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapPost("/{taskId:guid}/retry", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ClaimsPrincipal principal,
            ICurrentTenant currentTenant,
            IIdentityPermissionEvaluator permissions,
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
                    BuildPreviewContext(userId, principal, permissions, currentTenant.Id),
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
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));

        group.MapPost("/{taskId:guid}/cancel", async (Guid taskId, CancelImportExportTaskRequest request,
            ImportExportTaskCancellationService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.CancelAsync(taskId, request.Version, token).ConfigureAwait(false), http))
            .WithName("importExportCancelImportTask").Produces<ImportExportTaskDetailResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .WithMetadata(new RequestSizeLimitAttribute(1024))
            .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksCancel));

        group.MapGet("/{taskId:guid}/error-receipt", async (
            Guid taskId,
            ImportExportTaskExecutionService executionService,
            ICurrentTenant currentTenant,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!SessionBindingSnapshotReader.TryReadTenant(httpContext.User, currentTenant.Id, out var binding))
                return mapper.Map(Result<TenantResourceFileContent>.Failure(ImportTaskAuthority.Denied()), httpContext);
            var result = await executionService
                .OpenErrorReceiptAsync(taskId, binding, cancellationToken)
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
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.ImportTasksExecute));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    internal static StaticImportPreviewContext BuildPreviewContext(
        Guid requestedByUserId,
        ClaimsPrincipal principal, IIdentityPermissionEvaluator permissions, Guid? tenantId)
    {
        var capabilityFlags = permissions.ResolvePermissions(principal)
            .Where(permission => permissions.HasPermission(principal, permission))
            .ToDictionary(permission => permission, _ => true, StringComparer.Ordinal);
        return new StaticImportPreviewContext(requestedByUserId, capabilityFlags) {
            SessionBinding = SessionBindingSnapshotReader.TryReadTenant(principal, tenantId, out var binding) ? binding : null };
    }
}
