using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>停止本人未来生成，不依赖当前发布/列授权；已被领取的输出不能被取消请求覆盖。</summary>
internal sealed class ReportingExportTaskCancellationService(IQueryExecutor queries, ICommandExecutor commands,
    ICurrentTenant tenant, ICurrentSessionAuthorization sessions, IClock clock, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<ReportingExportTaskDetailResponse>> CancelAsync(Guid taskId, long version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tenantId = tenant.Id;
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null || tenant.Id == Guid.Empty)
            return Result<ReportingExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "A tenant interaction session is required.", ErrorType.Forbidden));
        if (taskId == Guid.Empty || version < 1)
            return Result<ReportingExportTaskDetailResponse>.Failure(new(CommonErrorCodes.RequestInvalid, "A task and positive version are required.", ErrorType.Validation));
        var actor = await sessions.AuthorizeAsync(ReportingExportTaskPermissions.Cancel, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId != tenantId || tenant.Id != tenantId || !tenant.IsAvailable || tenant.IsHost)
            return Result<ReportingExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "The current session cannot cancel this task.", ErrorType.Forbidden));
        var args = new Dictionary<string, object?> { ["Id"] = taskId, ["UserId"] = actor.UserId };
        var state = await queries.QuerySingleOrDefaultAsync<ReportingTaskCancellationState>(ReportingExportTaskSql.FindOwnedCancellationState, args, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (tenant.Id != tenantId || !tenant.IsAvailable || tenant.IsHost) return Result<ReportingExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied, "The tenant session changed.", ErrorType.Forbidden));
        if (state is null) return Result<ReportingExportTaskDetailResponse>.Failure(NotFound());
        // 来源或原创建会话已失效时，当前本人仍可凭独立取消权限阻止未来调度；不能借此生成或下载。
        if (state.StatusKey != ReportingExportTaskStatusKeys.Canceled)
        {
            if (state.Version != version || state.StatusKey != ReportingExportTaskStatusKeys.Queued)
                return Result<ReportingExportTaskDetailResponse>.Failure(Conflict());
            args.Add("Version", version); args.Add("Now", clock.UtcNow);
            var affected = await commands.ExecuteAsync(ReportingExportTaskSql.CancelOwnedQueued, args, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (affected != 1)
                return Result<ReportingExportTaskDetailResponse>.Failure(Conflict());
        }
        var record = await queries.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(database.Value.Provider),
            ReportingSqlParameters.Create(("Id", taskId)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (record is null || record.RequestedByUserId != actor.UserId) return Result<ReportingExportTaskDetailResponse>.Failure(NotFound());
        return record.StatusKey == ReportingExportTaskStatusKeys.Canceled
            ? Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(record))
            : Result<ReportingExportTaskDetailResponse>.Failure(Conflict());
    }
    private static Error NotFound() => new(ReportingErrorCodes.ExportTaskNotFound, "The export task was not found.", ErrorType.NotFound);
    private static Error Conflict() => new(ReportingExportCancellationErrorCodes.Conflict,
        "The task changed or a Worker is generating it. Refresh its details.", ErrorType.Conflict);
}
