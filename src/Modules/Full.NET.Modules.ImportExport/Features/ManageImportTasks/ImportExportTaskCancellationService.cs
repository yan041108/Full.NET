using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>取消只停止本人未来调度，不回滚业务表；来源撤权后仍可凭独立取消权限停止本人待执行工作。</summary>
internal sealed class ImportExportTaskCancellationService(IQueryExecutor queries, ICommandExecutor commands,
    ICurrentTenant tenant, ICurrentSessionAuthorization sessions, IClock clock)
{
    internal async Task<Result<ImportExportTaskDetailResponse>> CancelAsync(Guid taskId, long version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tenantId = tenant.Id;
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null)
            return Result<ImportExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "A tenant interaction session is required.", ErrorType.Forbidden));
        if (taskId == Guid.Empty || version < 1)
            return Result<ImportExportTaskDetailResponse>.Failure(new(CommonErrorCodes.RequestInvalid, "A task and positive version are required.", ErrorType.Validation));
        var actor = await sessions.AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId != tenantId || tenant.Id != tenantId || !tenant.IsAvailable || tenant.IsHost)
            return Result<ImportExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "The current session cannot cancel this task.", ErrorType.Forbidden));
        var args = ImportExportSqlParameters.Create(("Id", taskId), ("UserId", actor.UserId));
        var task = await queries.QuerySingleOrDefaultAsync<ImportTaskCancellationState>(ImportExportTaskSql.FindOwnedCancellationState, args, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (tenant.Id != tenantId || !tenant.IsAvailable || tenant.IsHost) return Result<ImportExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "The tenant session changed.", ErrorType.Forbidden));
        if (task is null) return Result<ImportExportTaskDetailResponse>.Failure(NotFound());
        // 已经停止的同一本人任务可安全重读；不能把其他终态当成取消成功。
        if (task.StatusKey != ImportExportTaskStatusKeys.Canceled)
        {
            if (task.Version != version || task.StatusKey is not (ImportExportTaskStatusKeys.PreviewSucceeded
                or ImportExportTaskStatusKeys.Queued or ImportExportTaskStatusKeys.ExecutionPartial))
                return Result<ImportExportTaskDetailResponse>.Failure(Conflict());
            args.Add("Version", version); args.Add("Now", clock.UtcNow);
            var affected = await commands.ExecuteAsync(ImportExportTaskSql.CancelOwnedPending, args, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (affected != 1) return Result<ImportExportTaskDetailResponse>.Failure(Conflict());
        }
        var record = await queries.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
            ImportExportSqlParameters.Create(("Id", taskId)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return record is null || record.RequestedByUserId != actor.UserId
            ? Result<ImportExportTaskDetailResponse>.Failure(NotFound())
            : record.StatusKey != ImportExportTaskStatusKeys.Canceled ? Result<ImportExportTaskDetailResponse>.Failure(Conflict())
            : Result<ImportExportTaskDetailResponse>.Success(ImportExportTaskMapper.MapDetail(record));
    }

    private static Error NotFound() => new(ImportExportErrorCodes.TaskNotFound, "The import task was not found.", ErrorType.NotFound);
    private static Error Conflict() => new(ImportTaskCancellationErrorCodes.Conflict,
        "The task has changed or a Worker has claimed it. Refresh after the active batch ends.", ErrorType.Conflict);
}
