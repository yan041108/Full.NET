using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
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
    StaticImportSchemaRegistry registry,
    ImportExportExecutionAuthorization authorization,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ImportExportOptions> options)
{
    /// <summary>将预校验成功任务排队执行。</summary>
    public async Task<Result<ImportExportTaskDetailResponse>> QueueExecuteAsync(
        Guid taskId,
        StaticImportPreviewContext executionContext,
        SessionBindingSnapshot sessionBinding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!await IsAuthorizedAsync(task, executionContext, sessionBinding, cancellationToken).ConfigureAwait(false))
            return Result<ImportExportTaskDetailResponse>.Failure(PermissionDeniedError());

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
                sessionBinding,
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
        SessionBindingSnapshot sessionBinding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!await IsAuthorizedAsync(task, executionContext, sessionBinding, cancellationToken).ConfigureAwait(false))
            return Result<ImportExportTaskDetailResponse>.Failure(PermissionDeniedError());

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
                sessionBinding,
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
        SessionBindingSnapshot sessionBinding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (!await IsAuthorizedAsync(task, executionContext, sessionBinding, cancellationToken).ConfigureAwait(false))
            return Result<ImportExportTaskDetailResponse>.Failure(PermissionDeniedError());

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
                sessionBinding,
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
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var task = await LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Result<TenantResourceFileContent>.Failure(TaskNotFoundError());
        }

        // 回执包含原业务行信息，下载不得以通用执行权限替代创建人和 Schema 的当前授权。
        if (!await IsAuthorizedAsync(task, new StaticImportPreviewContext(binding.UserId,
                new Dictionary<string, bool>()), binding, cancellationToken).ConfigureAwait(false))
        {
            return Result<TenantResourceFileContent>.Failure(PermissionDeniedError());
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
        SessionBindingSnapshot sessionBinding,
        CancellationToken cancellationToken)
    {
        string? executionRowsJson;
        if (resetExecution)
        {
            executionRowsJson = ImportExportTaskMapper.SerializeExecutionState(
                new ImportExportExecutionStateDocument(
                    ImportExportExecutionAuthorization.FreezeCapabilities(registry.TryResolve(task.SchemaKey)!,
                        ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson).CapabilityFlags),
                    [], sessionBinding));
        }
        else
        {
            var state = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson);
            executionRowsJson = ImportExportTaskMapper.SerializeExecutionState(
                new ImportExportExecutionStateDocument(
                    ImportExportExecutionAuthorization.FreezeCapabilities(registry.TryResolve(task.SchemaKey)!, state.CapabilityFlags),
                    state.Rows, sessionBinding));
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

    /// <summary>恢复由原创建人的当前会话授权，保留原预览能力与业务回执身份。</summary>
    private Task<bool> IsAuthorizedAsync(ImportExportTaskRecord task, StaticImportPreviewContext context,
        SessionBindingSnapshot binding, CancellationToken cancellationToken)
    {
        var handler = registry.TryResolve(task.SchemaKey);
        var state = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson);
        return handler is null || task.TenantId != currentTenant.Id
            || context.RequestedByUserId != binding.UserId || task.RequestedByUserId != binding.UserId
            // 旧预览缺少能力集合时不能猜测原有效行；带附加能力的 Schema 必须重新上传预览。
            || (state.CapabilityFlags is null && handler.ExecutionCapabilityPermissions.Count > 0)
            ? Task.FromResult(false)
            : authorization.IsAllowedAsync(binding, task.TenantId, handler, state.CapabilityFlags, cancellationToken);
    }

    private static Error PermissionDeniedError() =>
        new(CommonErrorCodes.PermissionDenied, "The import execution session or permission is no longer valid.", ErrorType.Forbidden);

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
