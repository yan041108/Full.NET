using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Platform.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Platform.Features.ManageBackupExecutor;

/// <summary>授权备份执行器任务目录、运行结果与受控下载 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册授权备份执行器相关路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/backup-executor")
            .WithTags("PlatformBackupExecutor");

        group.MapGet("/status", async (
            BackupExecutorStatusService statusService,
            CancellationToken cancellationToken) =>
        {
            var result = await statusService.GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("platformGetBackupExecutorStatus")
        .Produces<BackupExecutorStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupTasksRead));

        group.MapGet("/tasks", async (
            BackupTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("platformListBackupTasks")
        .Produces<IReadOnlyList<BackupTaskResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupTasksRead));

        group.MapGet("/tasks/{taskId:guid}", async (
            Guid taskId,
            BackupTaskQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(taskId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("platformGetBackupTask")
        .Produces<BackupTaskResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupTasksRead));

        group.MapGet("/runs", async (
            int? page,
            int? pageSize,
            Guid? taskId,
            string? status,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            BackupRunQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    taskId,
                    status,
                    fromUtc,
                    toUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("platformListBackupRuns")
        .Produces<PagedResult<BackupRunResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupRunsRead));

        group.MapGet("/runs/{runId:guid}", async (
            Guid runId,
            BackupRunQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("platformGetBackupRun")
        .Produces<BackupRunResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupRunsRead));

        group.MapGet("/runs/{runId:guid}/download", async (
            Guid runId,
            BackupRunArtifactService artifactService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await artifactService.OpenDownloadAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var download = result.Value!;
            return Results.File(
                download.Content,
                download.ContentType,
                download.FileName,
                download.LastModifiedUtc,
                enableRangeProcessing: true);
        })
        .WithName("platformDownloadBackupRunArtifact")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PlatformPermissions.BackupRunsDownload));
    }
}
