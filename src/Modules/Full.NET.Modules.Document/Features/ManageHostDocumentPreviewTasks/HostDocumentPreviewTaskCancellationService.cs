using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>在转换领取前取消创建人自己的 Host 任务，既有转换和文件保持原有所有权。</summary>
internal sealed class HostDocumentPreviewTaskCancellationService(
    IQueryExecutor query, ICommandExecutor command, ICurrentTenant tenant,
    IBackgroundSessionAuthorization authorization, IClock clock)
{
    /// <summary>通过创建人和版本原子取消排队任务；并发取消幂等，领取胜出返回冲突。</summary>
    /// <param name="taskId">预览任务标识。</param>
    /// <param name="binding">从 Host 交互请求冻结的当前会话。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    public async Task<Result<HostDocumentPreviewTaskResponse>> CancelAsync(
        Guid taskId, SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsAvailable || !tenant.IsHost || tenant.Id is not null
            || binding.TenantId is not null || binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.SecurityStamp) || binding.ActorScope != "host" || binding.EffectiveScope != "host") return Forbidden();
        var task = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null) return NotFound();
        if (task.RequestedByUserId != binding.UserId) return Forbidden();
        var actor = await authorization.AuthorizeAsync(binding, HostDocumentPreviewTaskPermissions.Cancel, cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.UserId != binding.UserId || actor.TenantId is not null || actor.SessionId != binding.SessionId
            || !tenant.IsAvailable || !tenant.IsHost || tenant.Id is not null) return Forbidden();
        cancellationToken.ThrowIfCancellationRequested();
        if (task.StatusKey == HostDocumentPreviewTaskStatusKeys.Cancelled) return Success(task);
        if (task.StatusKey != HostDocumentPreviewTaskStatusKeys.Pending) return Conflict();
        _ = await command.ExecuteAsync(DocumentPreviewTaskSql.Cancel, DocumentSqlParameters.Create(
            ("Id", taskId), ("RequestedByUserId", binding.UserId), ("ExpectedVersion", task.Version),
            ("CompletedAtUtc", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        var current = await LoadAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (current is null) return NotFound();
        if (!tenant.IsHost || tenant.Id is not null || current.RequestedByUserId != binding.UserId) return Forbidden();
        return current.StatusKey == HostDocumentPreviewTaskStatusKeys.Cancelled ? Success(current) : Conflict();
    }

    private Task<DocumentPreviewTaskRecord?> LoadAsync(Guid id, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, DocumentSqlParameters.Create(("Id", id)), token);
    private static Result<HostDocumentPreviewTaskResponse> Success(DocumentPreviewTaskRecord task) =>
        Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(task));
    private static Result<HostDocumentPreviewTaskResponse> NotFound() => Result<HostDocumentPreviewTaskResponse>.Failure(
        new(DocumentErrorCodes.PreviewTaskNotFound, "The preview task was not found.", ErrorType.NotFound));
    private static Result<HostDocumentPreviewTaskResponse> Forbidden() => Result<HostDocumentPreviewTaskResponse>.Failure(
        new(CommonErrorCodes.PermissionDenied, "The preview cancellation session or permission is no longer valid.", ErrorType.Forbidden));
    private static Result<HostDocumentPreviewTaskResponse> Conflict() => Result<HostDocumentPreviewTaskResponse>.Failure(
        new(DocumentErrorCodes.PreviewTaskStatusInvalid, "The preview task changed or has already been claimed.", ErrorType.Conflict));
}
