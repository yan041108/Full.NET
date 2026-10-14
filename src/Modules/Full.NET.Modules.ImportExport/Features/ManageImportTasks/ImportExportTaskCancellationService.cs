using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>在安全检查点取消创建人自己的任务，不回滚已提交行，也不强停在途批次。</summary>
internal sealed class ImportExportTaskCancellationService(
    IQueryExecutor query,
    ICommandExecutor command,
    ICurrentTenant tenant,
    IBackgroundSessionAuthorization authorization,
    IClock clock)
{
    /// <summary>按状态和版本原子结束任务；并发取消幂等，领取或重试胜出时返回冲突。</summary>
    public async Task<Result<ImportExportTaskDetailResponse>> CancelAsync(
        Guid taskId, SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null)
            throw new TenantContextMissingException("import_export.tenant_context_required");
        var tenantId = tenant.Id.Value;
        var task = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!IsOriginalTenant(tenantId)) return Forbidden();
        if (task is null) return NotFound();
        if (task.TenantId != tenantId || binding.TenantId != tenantId || task.RequestedByUserId != binding.UserId
            || binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(binding.SecurityStamp))
            return Forbidden();
        // 停止未来写入只要求取消权限；不借此重新授予已撤销的 Schema 执行能力。
        var actor = await authorization.AuthorizeAsync(binding, ImportExportPermissions.ImportTasksCancel, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor is null || actor.UserId != binding.UserId || actor.TenantId != tenantId || actor.SessionId != binding.SessionId
            || !IsOriginalTenant(tenantId))
            return Forbidden();
        if (task.StatusKey == ImportExportTaskStatusKeys.Cancelled) return Success(task);
        if (task.StatusKey is not (ImportExportTaskStatusKeys.PreviewSucceeded or ImportExportTaskStatusKeys.Queued
            or ImportExportTaskStatusKeys.ExecutionPartial or ImportExportTaskStatusKeys.ExecutionFailed))
            return Conflict();

        var affected = await command.ExecuteAsync(ImportExportTaskSql.Cancel, ImportExportSqlParameters.Create(
            ("Id", taskId), ("RequestedByUserId", binding.UserId), ("ExpectedVersion", task.Version),
            ("ExpectedStatusKey", task.StatusKey), ("ExecutionCompletedAtUtc", clock.UtcNow)), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOriginalTenant(tenantId)) return Forbidden();
        var current = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!IsOriginalTenant(tenantId)) return Forbidden();
        if (current is null) return NotFound();
        // CAS 失败只接受另一请求已取消的同一归属任务，不替换版本重试写入。
        if (current.TenantId != tenantId || current.RequestedByUserId != binding.UserId) return Forbidden();
        return (affected == 1 || current.StatusKey == ImportExportTaskStatusKeys.Cancelled)
            && current.StatusKey == ImportExportTaskStatusKeys.Cancelled ? Success(current) : Conflict();
    }

    private async Task<ImportExportTaskRecord?> LoadAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var task = await query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
            ImportExportSqlParameters.Create(("Id", id)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return task;
    }
    private bool IsOriginalTenant(Guid tenantId) => tenant.IsAvailable && !tenant.IsHost && tenant.Id == tenantId;
    private static Result<ImportExportTaskDetailResponse> Success(ImportExportTaskRecord task) =>
        Result<ImportExportTaskDetailResponse>.Success(ImportExportTaskMapper.MapDetail(task));
    private static Result<ImportExportTaskDetailResponse> NotFound() => Result<ImportExportTaskDetailResponse>.Failure(
        new(ImportExportErrorCodes.TaskNotFound, "The import task was not found.", ErrorType.NotFound));
    private static Result<ImportExportTaskDetailResponse> Forbidden() => Result<ImportExportTaskDetailResponse>.Failure(
        new(CommonErrorCodes.PermissionDenied, "The import cancellation session or permission is no longer valid.", ErrorType.Forbidden));
    private static Result<ImportExportTaskDetailResponse> Conflict() => Result<ImportExportTaskDetailResponse>.Failure(
        new(ImportExportErrorCodes.TaskStatusInvalid, "The import task changed or is not at a cancellable checkpoint.", ErrorType.Conflict));
}
