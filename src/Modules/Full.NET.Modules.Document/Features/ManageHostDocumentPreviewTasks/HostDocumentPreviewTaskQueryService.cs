using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>Host 文档预览转换任务只读查询。</summary>
internal sealed class HostDocumentPreviewTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IHostFileContentReader hostFileContentReader,
    IOptions<DatabaseOptions> databaseOptions,
    IDocumentAccessPolicy accessPolicy,
    DocumentAccessLogRecorder logs,
    IHostFileDescriptorReader files)
{
    public async Task<Result<PagedResult<HostDocumentPreviewTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? documentItemId,
        string? statusKey = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        statusKey = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        if (statusKey is not null && !HostDocumentPreviewTaskStatusKeys.All.Contains(statusKey, StringComparer.Ordinal))
            return Result<PagedResult<HostDocumentPreviewTaskResponse>>.Failure(
                new Error(CommonErrorCodes.RequestInvalid, "A supported preview task status is required.", ErrorType.Validation));
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentPreviewTaskSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentPreviewTaskSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor
            .QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(
                    ("DocumentItemId", documentItemId),
                    ("StatusKey", statusKey),
                    ("ActorUserId", accessPolicy.ActorUserId),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentPreviewTaskRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var items = pageResult.Rows.Select(HostDocumentPreviewTaskMapper.Map).ToArray();
        return Result<PagedResult<HostDocumentPreviewTaskResponse>>.Success(
            new PagedResult<HostDocumentPreviewTaskResponse>(items, page, pageSize, pageResult.Total));
    }

    public async Task<Result<HostDocumentPreviewTaskResponse>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await queryExecutor.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
            DocumentPreviewTaskSql.FindById, DocumentSqlParameters.Create(("Id", taskId)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (task is null) return Result<HostDocumentPreviewTaskResponse>.Failure(TaskNotFoundError());
        var error = await accessPolicy.CheckAsync(task.DocumentItemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (error is not null)
            return Result<HostDocumentPreviewTaskResponse>.Failure(error);
        var response = HostDocumentPreviewTaskMapper.Map(task);
        if (task.StatusKey != HostDocumentPreviewTaskStatusKeys.Succeeded || task.OutputFileId is not { } fileId)
            return Result<HostDocumentPreviewTaskResponse>.Success(response);
        try
        {
            var state = task.OutputReferenceId is { } referenceId
                ? await queryExecutor.QuerySingleOrDefaultAsync<string>(DocumentPreviewReferenceSql.FindOutputState,
                    DocumentSqlParameters.Create(("ReferenceId", referenceId), ("TaskId", taskId),
                        ("DocumentId", task.DocumentItemId), ("FileId", fileId)), cancellationToken).ConfigureAwait(false)
                : null;
            var availability = state == "released" ? FileAvailabilityKeys.Released
                : (task.OutputReferenceId is null || state == "claimed")
                    && await files.GetReadyDescriptorAsync(fileId, cancellationToken).ConfigureAwait(false) is not null
                    ? FileAvailabilityKeys.Registered : null;
            if (availability is not null)
            {
                error = await accessPolicy.CheckAsync(task.DocumentItemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (error is not null) return Result<HostDocumentPreviewTaskResponse>.Failure(error);
                response = response with { OutputAvailabilityKey = availability };
            }
        }
        catch (ServiceCapacityExceededException)
        {
            // 结果状态读取繁忙时保留已授权任务的执行事实。
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Result<HostDocumentPreviewTaskResponse>.Success(response);
    }

    internal async Task<Result<HostDocumentPreviewTaskResponse>> GetAuthorizedAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
                DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return record is null
            ? Result<HostDocumentPreviewTaskResponse>.Failure(TaskNotFoundError())
            : Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(record));
    }

    public async Task<Result<HostFileContent>> OpenOutputContentAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await queryExecutor.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
            DocumentPreviewTaskSql.FindById, DocumentSqlParameters.Create(("Id", taskId)), cancellationToken).ConfigureAwait(false);
        if (task is null) return Result<HostFileContent>.Failure(TaskNotFoundError());
        if (await accessPolicy.CheckAsync(task.DocumentItemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false) is { } error)
            return Result<HostFileContent>.Failure(error);
        // 状态判断和打开文件使用同一已授权快照，避免完成竞态把旧空输出与新结果比较成 404。
        var content = await OpenOutputSnapshotAsync(task, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess) return content;
        var transferred = false;
        try
        {
            if (await accessPolicy.CheckAsync(task.DocumentItemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false) is { } denied)
                return Result<HostFileContent>.Failure(denied);
            var item = await queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", task.DocumentItemId)), cancellationToken).ConfigureAwait(false);
            var current = await queryExecutor.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)), cancellationToken).ConfigureAwait(false);
            if (item is null || current?.StatusKey != HostDocumentPreviewTaskStatusKeys.Succeeded || current.OutputFileId != task.OutputFileId)
                return Result<HostFileContent>.Failure(TaskNotFoundError());
            await logs.RecordAsync(task.DocumentItemId, task.DocumentTitle, HostDocumentAccessTypeKeys.Preview,
                HostDocumentAccessSourceKeys.Authenticated, accessPolicy.ActorUserId, null, cancellationToken, taskId).ConfigureAwait(false);
            transferred = true;
            return content;
        }
        finally { if (!transferred) await content.Value!.Content.DisposeAsync().ConfigureAwait(false); }
    }

    internal async Task<Result<HostFileContent>> OpenOutputContentAuthorizedAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
                DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null)
        {
            return Result<HostFileContent>.Failure(TaskNotFoundError());
        }

        return await OpenOutputSnapshotAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>只有已成功的固定输出快照可以打开；未就绪返回稳定业务错误，不能混用两次读取的字段。</summary>
    private async Task<Result<HostFileContent>> OpenOutputSnapshotAsync(DocumentPreviewTaskRecord record, CancellationToken cancellationToken)
    {
        if (!string.Equals(record.StatusKey, HostDocumentPreviewTaskStatusKeys.Succeeded, StringComparison.Ordinal)
            || record.OutputFileId is null)
        {
            return Result<HostFileContent>.Failure(
                new Error(
                    DocumentErrorCodes.PreviewTaskNotReady,
                    "Document preview task output is not ready.",
                    ErrorType.BusinessRule));
        }

        var content = await hostFileContentReader
            .OpenReadyContentAsync(record.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!content.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return content;
        }

        // 文件端口交付流后由本服务持有，晚取消时不能把释放责任留给尚未接收的响应。
        var transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transferred = true;
            return content;
        }
        finally
        {
            if (!transferred) await content.Value!.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Error TaskNotFoundError() =>
        new(DocumentErrorCodes.PreviewTaskNotFound, "Document preview task was not found.", ErrorType.NotFound);
}
