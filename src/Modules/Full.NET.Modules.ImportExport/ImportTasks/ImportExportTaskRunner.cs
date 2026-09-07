using Full.NET.Abstractions.Ids;
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

/// <summary>领取 queued 或租约到期的导入任务，并按检查点调用 Schema 处理器。</summary>
/// <param name="queryExecutor">受租户守卫保护的读执行器。</param>
/// <param name="commandExecutor">受租户守卫保护的写执行器。</param>
/// <param name="transaction">MySQL 领取所需的短事务。</param>
/// <param name="resourceFiles">租户资源文件存储。</param>
/// <param name="registry">静态 Schema 处理器目录。</param>
/// <param name="tenantResolver">活动租户解析。</param>
/// <param name="currentTenant">当前租户写入器。</param>
/// <param name="clock">时钟。</param>
/// <param name="idGenerator">租约 UUID 生成器。</param>
/// <param name="databaseOptions">数据库提供程序。</param>
/// <param name="options">导入执行配置。</param>
internal sealed class ImportExportTaskRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ITenantResourceFileStore resourceFiles,
    StaticImportSchemaRegistry registry,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
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
        var now = clock.UtcNow;
        var pendingTenantIds = await queryExecutor.QueryAsync<Guid>(
            databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? ImportExportTaskSql.ListPendingTenantIdsSqlServer
                : ImportExportTaskSql.ListPendingTenantIdsMySql,
            ImportExportSqlParameters.Create(("BatchSize", batchSize), ("Now", now)), cancellationToken).ConfigureAwait(false);
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

                while (processed < batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // ProcessOne 结束会清理上下文；每次领取前必须重新进入活动租户。
                    if (!await TrySetTenantScopeAsync(tenantId, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    var claimed = await ClaimOneAsync(cancellationToken).ConfigureAwait(false);
                    if (claimed is null)
                    {
                        break;
                    }

                    await ProcessOneAsync(claimed, cancellationToken).ConfigureAwait(false);
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
                    task,
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
                    task,
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
                ?? new Dictionary<string, bool>(StringComparer.Ordinal)) { TaskId = task.Id };
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
                    task,
                    batchResult.Error!.Code,
                    clock.UtcNow,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await ApplyBatchResultAsync(task, batchResult.Value!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 宿主取消不能写成失败；租约到期后由其他实例从检查点重领。
        }
        catch (Exception)
        {
            // 批次可能已写入业务数据但进度未提交；保留 executing 供租约到期后按同一 NextLineNumber 重试。
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    /// <summary>合并本批检查点；完成时复用已上传的错误回执，避免上传成功后崩溃导致重复对象。</summary>
    /// <param name="task">领取快照。</param>
    /// <param name="batch">本批行结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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

            if (executionFailedRowCount > 0 && errorReceiptFileId is null)
            {
                errorReceiptFileId = await ResolveOrUploadErrorReceiptAsync(task, mergedRows, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            statusKey = ImportExportTaskStatusKeys.Queued;
        }

        var affected = await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.UpdateExecutionProgress,
                ImportExportSqlParameters.Create(
                    ("Id", task.Id),
                    ("LeaseId", task.LeaseId),
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
        if (affected == 0)
        {
            // 租约被抢走或任务已终态完成；重复完成必须保持幂等，不得再写检查点。
            var current = await queryExecutor
                .QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                    ImportExportTaskSql.FindById,
                    ImportExportSqlParameters.Create(("Id", task.Id)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (current is not null && IsTerminal(current.StatusKey))
            {
                return;
            }
        }
    }

    /// <summary>优先绑定已存在的回执文件，只有资源上还没有额外就绪对象时才上传。</summary>
    /// <param name="task">任务快照。</param>
    /// <param name="mergedRows">已合并的执行行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Guid?> ResolveOrUploadErrorReceiptAsync(
        ImportExportTaskRecord task,
        IReadOnlyList<StaticImportRowExecutionResult> mergedRows,
        CancellationToken cancellationToken)
    {
        var ready = await resourceFiles.ListReadyAsync("import_export", task.Id, cancellationToken)
            .ConfigureAwait(false);
        var existing = ready.LastOrDefault(item => item.FileId != task.SourceFileId);
        if (existing is not null)
        {
            return existing.FileId;
        }

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
        return upload.IsSuccess ? upload.Value!.FileId : null;
    }

    /// <summary>仅在仍持有当前租约时写入任务级失败。</summary>
    /// <param name="task">领取快照。</param>
    /// <param name="errorCode">稳定错误码。</param>
    /// <param name="completedAtUtc">完成时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task MarkExecutionFailedAsync(
        ImportExportTaskRecord task,
        string errorCode,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.MarkExecutionFailed,
                ImportExportSqlParameters.Create(
                    ("Id", task.Id),
                    ("LeaseId", task.LeaseId),
                    ("ErrorCode", errorCode),
                    ("ExecutionCompletedAtUtc", completedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>绑定活动租户；失败时不得沿用调用方残留上下文。</summary>
    /// <param name="tenantId">任务所属租户。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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

    /// <summary>领取一条排队或租约到期任务，写入新的执行租约。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<ImportExportTaskRecord?> ClaimOneAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var leaseId = idGenerator.NewId();
        var leaseExpiresAt = now.AddSeconds(Math.Clamp(options.CurrentValue.LeaseSeconds, 30, 3600));
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var claimed = await queryExecutor.QueryAsync<ImportExportTaskRecord>(
                    ImportExportTaskSql.ClaimQueuedSqlServer,
                    ImportExportSqlParameters.Create(
                        ("Now", now),
                        ("LeaseId", leaseId),
                        ("LeaseExpiresAtUtc", leaseExpiresAt)),
                    cancellationToken)
                .ConfigureAwait(false);
            return claimed.Count == 0 ? null : claimed[0];
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            ImportExportTaskSql.SelectClaimableIdsMySql,
                            ImportExportSqlParameters.Create(("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return null;
                    }

                    await commandExecutor.ExecuteAsync(
                            ImportExportTaskSql.ClaimByIdsMySql,
                            ImportExportSqlParameters.Create(
                                ("Ids", ids.ToArray()),
                                ("Now", now),
                                ("LeaseId", leaseId),
                                ("LeaseExpiresAtUtc", leaseExpiresAt)),
                            token)
                        .ConfigureAwait(false);
                    var rows = await queryExecutor.QueryAsync<ImportExportTaskRecord>(
                            ImportExportTaskSql.SelectByIds,
                            ImportExportSqlParameters.Create(("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                    return rows.Count == 0 ? null : rows[0];
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>终态任务禁止再被领取或覆盖检查点。</summary>
    /// <param name="statusKey">当前状态。</param>
    private static bool IsTerminal(string statusKey) =>
        statusKey is ImportExportTaskStatusKeys.ExecutionSucceeded
            or ImportExportTaskStatusKeys.ExecutionPartial
            or ImportExportTaskStatusKeys.ExecutionFailed;

    /// <summary>由源文件名派生错误回执下载名，不作为存储路径。</summary>
    /// <param name="task">任务快照。</param>
    private static string BuildErrorReceiptFileName(ImportExportTaskRecord task)
    {
        var baseName = string.IsNullOrWhiteSpace(task.SourceFileName)
            ? "import-task"
            : Path.GetFileNameWithoutExtension(task.SourceFileName);
        return $"{baseName}-errors.xlsx";
    }
}
