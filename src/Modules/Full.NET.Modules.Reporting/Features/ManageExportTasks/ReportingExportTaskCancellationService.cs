using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>在 Worker 领取前取消创建人自己的任务，不强停在途外部查询或删除文件。</summary>
internal sealed class ReportingExportTaskCancellationService(
    IQueryExecutor query, ICommandExecutor command, ICurrentTenant tenant,
    IBackgroundSessionAuthorization authorization, IClock clock, IOptions<DatabaseOptions> database)
{
    /// <summary>按租户、创建人及版本原子取消；重复取消幂等，领取胜出返回冲突。</summary>
    /// <param name="taskId">导出任务标识。</param>
    /// <param name="binding">已认证请求的当前交互会话。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    public async Task<Result<ReportingExportTaskDetailResponse>> CancelAsync(
        Guid taskId, SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null)
            throw new TenantContextMissingException("reporting.tenant_context_required");
        var tenantId = tenant.Id.Value;
        var task = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null) return NotFound();
        if (task.TenantId != tenantId || binding.TenantId != tenantId || task.RequestedByUserId != binding.UserId
            || binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(binding.SecurityStamp))
            return Forbidden();
        // 停止未来执行只复核独立取消权限，不要求重新授予原报表版本或受保护列权限。
        var actor = await authorization.AuthorizeAsync(binding, ReportingExportTaskPermissions.Cancel, cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.UserId != binding.UserId || actor.TenantId != tenantId || actor.SessionId != binding.SessionId
            || tenant.Id != tenantId || tenant.IsHost)
            return Forbidden();
        cancellationToken.ThrowIfCancellationRequested();
        if (task.StatusKey == ReportingExportTaskStatusKeys.Cancelled) return Success(task);
        if (task.StatusKey != ReportingExportTaskStatusKeys.Queued) return Conflict();

        _ = await command.ExecuteAsync(ReportingExportTaskSql.Cancel, ReportingSqlParameters.Create(
            ("Id", taskId), ("RequestedByUserId", binding.UserId), ("ExpectedVersion", task.Version),
            ("CompletedAtUtc", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        var current = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (current is null) return NotFound();
        if (tenant.Id != tenantId || current.TenantId != tenantId || current.RequestedByUserId != binding.UserId) return Forbidden();
        // CAS 失败只接受同一归属的取消终态，不用新版本再次写入。
        return current.StatusKey == ReportingExportTaskStatusKeys.Cancelled ? Success(current) : Conflict();
    }

    private Task<ReportingExportTaskRecord?> LoadAsync(Guid id, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(database.Value.Provider),
            ReportingSqlParameters.Create(("Id", id)), token);
    private static Result<ReportingExportTaskDetailResponse> Success(ReportingExportTaskRecord task) =>
        Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(task));
    private static Result<ReportingExportTaskDetailResponse> NotFound() => Result<ReportingExportTaskDetailResponse>.Failure(
        new(ReportingErrorCodes.ExportTaskNotFound, "The export task was not found.", ErrorType.NotFound));
    private static Result<ReportingExportTaskDetailResponse> Forbidden() => Result<ReportingExportTaskDetailResponse>.Failure(
        new(CommonErrorCodes.PermissionDenied, "The export cancellation session or permission is no longer valid.", ErrorType.Forbidden));
    private static Result<ReportingExportTaskDetailResponse> Conflict() => Result<ReportingExportTaskDetailResponse>.Failure(
        new(ReportingErrorCodes.ExportTaskStatusInvalid, "The export task changed or has already been claimed.", ErrorType.Conflict));
}
