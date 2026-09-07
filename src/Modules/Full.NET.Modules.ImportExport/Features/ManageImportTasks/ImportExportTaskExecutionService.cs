using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务排队执行、恢复、重试与错误回执下载。</summary>
internal sealed class ImportExportTaskExecutionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ITenantResourceFileStore resourceFiles,
    ICurrentTenant currentTenant,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ImportExportOptions> options)
{
    /// <summary>将预校验成功任务排队执行。</summary>
    public async Task<Result<ImportExportTaskDetailResponse>> QueueExecuteAsync(
        Guid taskId,
        StaticImportPreviewContext executionContext,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!string.Equals(task.StatusKey, ImportExportTaskStatusKeys.PreviewSucceeded, StringComparison.Ordinal))
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        var updated = await TransitionAsync(
                task,
                ImportExportTaskStatusKeys.PreviewSucceeded,
                ImportExportTaskStatusKeys.Queued,
                resetExecution: true,
                executionContext,
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        await RunSynchronouslyIfConfiguredAsync(taskId, cancellationToken).ConfigureAwait(false);
        return await ReloadDetailAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从部分成功检查点恢复排队执行。</summary>
    public async Task<Result<ImportExportTaskDetailResponse>> ResumeAsync(
        Guid taskId,
        StaticImportPreviewContext executionContext,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!string.Equals(task.StatusKey, ImportExportTaskStatusKeys.ExecutionPartial, StringComparison.Ordinal))
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        var updated = await TransitionAsync(
                task,
                ImportExportTaskStatusKeys.ExecutionPartial,
                ImportExportTaskStatusKeys.Queued,
                resetExecution: false,
                executionContext,
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        await RunSynchronouslyIfConfiguredAsync(taskId, cancellationToken).ConfigureAwait(false);
        return await ReloadDetailAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>重置执行状态并重新排队。</summary>
    public async Task<Result<ImportExportTaskDetailResponse>> RetryAsync(
        Guid taskId,
        StaticImportPreviewContext executionContext,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!string.Equals(task.StatusKey, ImportExportTaskStatusKeys.ExecutionPartial, StringComparison.Ordinal)
            && !string.Equals(task.StatusKey, ImportExportTaskStatusKeys.ExecutionFailed, StringComparison.Ordinal))
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        var updated = await TransitionAsync(
                task,
                task.StatusKey,
                ImportExportTaskStatusKeys.Queued,
                resetExecution: true,
                executionContext,
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(StatusInvalidError());
        }

        await RunSynchronouslyIfConfiguredAsync(taskId, cancellationToken).ConfigureAwait(false);
        return await ReloadDetailAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>打开错误回执 xlsx 内容。</summary>
    public async Task<Result<TenantResourceFileContent>> OpenErrorReceiptAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<TenantResourceFileContent>.Failure(TaskNotFoundError());
        }

        if (task.ErrorReceiptFileId is null || task.ExecutionFailedRowCount <= 0)
        {
            return Result<TenantResourceFileContent>.Failure(ErrorReceiptNotReadyError());
        }

        return await resourceFiles
            .OpenReadyContentAsync("import_export", task.Id, task.ErrorReceiptFileId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunSynchronouslyIfConfiguredAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.RunSynchronously)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ImportExportTaskRunner>();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processed = await runner.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
            if (processed == 0)
            {
                break;
            }

            var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                break;
            }

            if (task.StatusKey is ImportExportTaskStatusKeys.ExecutionSucceeded
                or ImportExportTaskStatusKeys.ExecutionPartial
                or ImportExportTaskStatusKeys.ExecutionFailed)
            {
                break;
            }
        }
    }

    private async Task<Result<ImportExportTaskDetailResponse>> ReloadDetailAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                ImportExportTaskSql.FindById,
                ImportExportSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ImportExportTaskDetailResponse>.Success(ImportExportTaskMapper.MapDetail(record));
    }

    private async Task<ImportExportTaskRecord?> TransitionAsync(
        ImportExportTaskRecord task,
        string expectedStatusKey,
        string targetStatusKey,
        bool resetExecution,
        StaticImportPreviewContext executionContext,
        CancellationToken cancellationToken)
    {
        string? executionRowsJson;
        if (resetExecution)
        {
            executionRowsJson = ImportExportTaskMapper.SerializeExecutionState(
                new ImportExportExecutionStateDocument(
                    executionContext.CapabilityFlags.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal),
                    []));
        }
        else
        {
            var state = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson);
            executionRowsJson = ImportExportTaskMapper.SerializeExecutionState(
                new ImportExportExecutionStateDocument(
                    executionContext.CapabilityFlags.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal),
                    state.Rows));
        }

        var affected = await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.QueueExecution,
                ImportExportSqlParameters.Create(
                    ("Id", task.Id),
                    ("ExpectedStatusKey", expectedStatusKey),
                    ("StatusKey", targetStatusKey),
                    ("NextLineNumber", resetExecution ? 0 : task.NextLineNumber),
                    ("ProcessedRowCount", resetExecution ? 0 : task.ProcessedRowCount),
                    ("SucceededRowCount", resetExecution ? 0 : task.SucceededRowCount),
                    ("ExecutionFailedRowCount", resetExecution ? 0 : task.ExecutionFailedRowCount),
                    ("ExecutionRowsJson", executionRowsJson),
                    ("ErrorReceiptFileId", resetExecution ? null : task.ErrorReceiptFileId),
                    ("ExecutionStartedAtUtc", resetExecution ? null : task.ExecutionStartedAtUtc),
                    ("ExecutionCompletedAtUtc", resetExecution ? null : task.ExecutionCompletedAtUtc),
                    ("ErrorCode", resetExecution ? null : task.ErrorCode)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return null;
        }

        return await LoadTaskAsync(task.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportExportTaskRecord?> LoadTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken) =>
        await queryExecutor
            .QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                ImportExportTaskSql.FindById,
                ImportExportSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("import_export.tenant_context_required");
        }
    }

    private static Error TaskNotFoundError() =>
        new(ImportExportErrorCodes.TaskNotFound, "The import task was not found.", ErrorType.NotFound);

    private static Error StatusInvalidError() =>
        new(
            ImportExportErrorCodes.TaskStatusInvalid,
            "The import task status does not allow this operation.",
            ErrorType.BusinessRule);

    private static Error ErrorReceiptNotReadyError() =>
        new(
            ImportExportErrorCodes.ErrorReceiptNotReady,
            "The import task error receipt is not ready.",
            ErrorType.BusinessRule);
}
