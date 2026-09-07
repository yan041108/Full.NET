using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.ImportTasks;

/// <summary>领取 queued 导入任务并按批调用 Schema 处理器执行写入。</summary>
internal sealed class ImportExportTaskRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ITenantResourceFileStore resourceFiles,
    StaticImportSchemaRegistry registry,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptionsMonitor<ImportExportOptions> options)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>先发现租户，再按活动租户领取任务；每个任务最多处理一个执行批次。</summary>
    /// <param name="cancellationToken">停止领取和执行的取消令牌。</param>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(options.CurrentValue.BatchSize, 1, 200);
        var pendingTenantIds = await queryExecutor.QueryAsync<Guid>(
            databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? ImportExportTaskSql.ListPendingTenantIdsSqlServer
                : ImportExportTaskSql.ListPendingTenantIdsMySql,
            ImportExportSqlParameters.Create(("BatchSize", batchSize)), cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var tenantId in pendingTenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processed >= batchSize)
            {
                break;
            }

            try
            {
                // 调度目录不是租户授权；停用或不存在的租户不能领取、读取或执行任务。
                if (!await TrySetTenantScopeAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var claimed = await ClaimAsync(batchSize - processed, cancellationToken).ConfigureAwait(false);
                foreach (var task in claimed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessOneAsync(task, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        return processed;
    }

    /// <summary>在任务归属的活动租户中执行单个批次，并始终清理上下文。</summary>
    /// <param name="task">已领取的任务快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ProcessOneAsync(
        ImportExportTaskRecord task,
        CancellationToken cancellationToken)
    {
        if (!await TrySetTenantScopeAsync(task.TenantId, cancellationToken).ConfigureAwait(false))
        {
            // 失去活动租户资格时不能使用旧上下文写入任务；恢复由重新授权后的调度处理。
            currentTenant.Clear();
            return;
        }

        try
        {
            var handler = registry.TryResolve(task.SchemaKey);
            if (handler is null)
            {
                await MarkExecutionFailedAsync(
                    task.Id,
                    ImportExportErrorCodes.SchemaNotFound,
                    clock.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var sourceResult = await resourceFiles
                .OpenReadyContentAsync("import_export", task.Id, task.SourceFileId, cancellationToken)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                await MarkExecutionFailedAsync(
                    task.Id,
                    sourceResult.Error!.Code,
                    clock.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var source = sourceResult.Value!;
            var contentLength = source.Content.CanSeek ? source.Content.Length : options.CurrentValue.MaxUploadBytes;
            var executionBatchSize = Math.Clamp(options.CurrentValue.BatchSize, 1, 200);
            var executionState = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson);
            var previewContext = new StaticImportPreviewContext(
                task.RequestedByUserId,
                executionState.CapabilityFlags
                ?? new Dictionary<string, bool>(StringComparer.Ordinal));
            Result<StaticImportBatchExecutionResult> batchResult;
            try
            {
                batchResult = await handler.ExecuteBatchAsync(
                        source.Content,
                        contentLength,
                        task.NextLineNumber,
                        executionBatchSize,
                        previewContext,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await source.Content.DisposeAsync().ConfigureAwait(false);
            }

            if (!batchResult.IsSuccess)
            {
                await MarkExecutionFailedAsync(
                    task.Id,
                    batchResult.Error!.Code,
                    clock.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await ApplyBatchResultAsync(task, batchResult.Value!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await MarkExecutionFailedAsync(
                task.Id,
                ImportExportErrorCodes.ExecutionFailed,
                clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private async Task ApplyBatchResultAsync(
        ImportExportTaskRecord task,
        StaticImportBatchExecutionResult batch,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var executionState = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson);
        var mergedRows = ImportExportTaskMapper.MergeExecutionRows(executionState.Rows, batch.Rows);
        var batchSucceeded = batch.Rows.Count(row => row.Succeeded);
        var batchFailed = batch.Rows.Count - batchSucceeded;
        var processedRowCount = task.ProcessedRowCount + batch.Rows.Count;
        var succeededRowCount = task.SucceededRowCount + batchSucceeded;
        var executionFailedRowCount = task.ExecutionFailedRowCount + batchFailed;
        var nextLineNumber = task.NextLineNumber + batch.Rows.Count;
        var executionComplete = nextLineNumber >= task.ValidRowCount;
        string statusKey;
        DateTimeOffset? executionCompletedAtUtc = null;
        Guid? errorReceiptFileId = task.ErrorReceiptFileId;
        string? errorCode = null;

        if (executionComplete)
        {
            executionCompletedAtUtc = now;
            if (executionFailedRowCount == 0)
            {
                statusKey = ImportExportTaskStatusKeys.ExecutionSucceeded;
            }
            else if (succeededRowCount > 0)
            {
                statusKey = ImportExportTaskStatusKeys.ExecutionPartial;
            }
            else
            {
                statusKey = ImportExportTaskStatusKeys.ExecutionFailed;
                errorCode = ImportExportErrorCodes.ExecutionFailed;
            }

            if (executionFailedRowCount > 0)
            {
                var failedRows = mergedRows.Where(row => !row.Succeeded).ToArray();
                var receiptBytes = ImportExportErrorReceiptRenderer.Render(failedRows);
                await using var receiptStream = new MemoryStream(receiptBytes, writable: false);
                var upload = await resourceFiles
                    .UploadAsync(
                        "import_export", task.Id, task.RequestedByUserId,
                        BuildErrorReceiptFileName(task),
                        WorkbookContentType,
                        receiptStream,
                        receiptBytes.Length,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (upload.IsSuccess)
                {
                    errorReceiptFileId = upload.Value!.FileId;
                }
            }
        }
        else
        {
            statusKey = ImportExportTaskStatusKeys.Queued;
        }

        await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.UpdateExecutionProgress,
                ImportExportSqlParameters.Create(
                    ("Id", task.Id),
                    ("StatusKey", statusKey),
                    ("ProcessedRowCount", processedRowCount),
                    ("SucceededRowCount", succeededRowCount),
                    ("ExecutionFailedRowCount", executionFailedRowCount),
                    ("NextLineNumber", nextLineNumber),
                    ("ExecutionRowsJson", ImportExportTaskMapper.SerializeExecutionState(
                        new ImportExportExecutionStateDocument(executionState.CapabilityFlags, mergedRows))),
                    ("ErrorReceiptFileId", errorReceiptFileId),
                    ("ExecutionCompletedAtUtc", executionCompletedAtUtc),
                    ("ErrorCode", errorCode)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkExecutionFailedAsync(
        Guid taskId,
        string errorCode,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.MarkExecutionFailed,
                ImportExportSqlParameters.Create(
                    ("Id", taskId),
                    ("ErrorCode", errorCode),
                    ("ExecutionCompletedAtUtc", completedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<bool> TrySetTenantScopeAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return false;
        }

        currentTenant.SetTenant(tenant);
        return true;
    }

    private async Task<IReadOnlyList<ImportExportTaskRecord>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            return await queryExecutor.QueryAsync<ImportExportTaskRecord>(
                    ImportExportTaskSql.ClaimQueuedSqlServer,
                    ImportExportSqlParameters.Create(("BatchSize", batchSize), ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            ImportExportTaskSql.SelectClaimableIdsMySql,
                            ImportExportSqlParameters.Create(("BatchSize", batchSize)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return Array.Empty<ImportExportTaskRecord>();
                    }

                    await commandExecutor.ExecuteAsync(
                            ImportExportTaskSql.ClaimByIdsMySql,
                            ImportExportSqlParameters.Create(("Ids", ids.ToArray()), ("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    return await queryExecutor.QueryAsync<ImportExportTaskRecord>(
                            ImportExportTaskSql.SelectByIds,
                            ImportExportSqlParameters.Create(("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildErrorReceiptFileName(ImportExportTaskRecord task)
    {
        var baseName = string.IsNullOrWhiteSpace(task.SourceFileName)
            ? "import-task"
            : Path.GetFileNameWithoutExtension(task.SourceFileName);
        return $"{baseName}-errors.xlsx";
    }
}
