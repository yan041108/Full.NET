using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.PreviewTasks;

/// <summary>领取并处理 Host 文档 Office 预览转换任务。</summary>
internal sealed class DocumentPreviewTaskRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IHostFileContentReader hostFileContentReader,
    IHostFileUploadWriter hostFileUploadWriter,
    DocumentOfficePreviewConversionProviderResolver providerResolver,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options)
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return 0;
        }

        var batchSize = Math.Clamp(currentOptions.BatchSize, 1, 50);
        await ExpireProcessingAsync(currentOptions, batchSize, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        while (processed < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 顺序处理只领取当前一个任务，后续任务不会在转换前等待并消耗其租约。
            var claimed = await ClaimAsync(1, currentOptions.EffectiveLeaseSeconds, cancellationToken).ConfigureAwait(false);
            if (claimed.Count == 0) break;
            await ProcessOneAsync(claimed[0], cancellationToken).ConfigureAwait(false);
            processed++;
        }
        await ExpireProcessingAsync(currentOptions, batchSize, cancellationToken).ConfigureAwait(false);
        return processed;
    }

    private async Task ProcessOneAsync(
        DocumentPreviewTaskRecord task,
        CancellationToken cancellationToken)
    {
        var snapshot = options.CurrentValue;
        var remaining = task.LeaseExpiresAtUtc!.Value - clock.UtcNow;
        if (remaining <= TimeSpan.Zero) return;
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        leaseCancellation.CancelAfter(remaining);
        var executionToken = leaseCancellation.Token;
        string? workingDirectory = null;
        try
        {
            EnsureActiveLease(task, executionToken);
            var sourceResult = await hostFileContentReader
                .OpenReadyContentAsync(task.SourceFileId, executionToken)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                await MarkFailedAsync(task, sourceResult.Error!.Code, executionToken).ConfigureAwait(false);
                return;
            }

            var source = sourceResult.Value!;
            try
            {
                EnsureActiveLease(task, executionToken);
                if (source.Content.CanSeek && source.Content.Length > snapshot.MaxInputBytes)
                {
                    await MarkFailedAsync(
                        task,
                        DocumentErrorCodes.OfficePreviewInputTooLarge,
                        executionToken).ConfigureAwait(false);
                    return;
                }

                workingDirectory = CreateWorkingDirectory(snapshot);
                var provider = providerResolver.ResolveCurrent();
                using var boundedInput = new DocumentPreviewBudgetStream(source.Content, snapshot.MaxInputBytes, DocumentErrorCodes.OfficePreviewInputTooLarge);
                var conversionResult = await provider
                    .ConvertAsync(
                        new DocumentOfficePreviewConversionInput(
                            source.OriginalFileName,
                            source.ContentType,
                            boundedInput,
                            source.Content.CanSeek ? source.Content.Length : snapshot.MaxInputBytes),
                        workingDirectory,
                        executionToken)
                    .ConfigureAwait(false);
                if (!conversionResult.IsSuccess)
                {
                    await MarkFailedAsync(task, conversionResult.Error!.Code, executionToken)
                        .ConfigureAwait(false);
                    return;
                }

                var output = conversionResult.Value!;
                try
                {
                    EnsureActiveLease(task, executionToken);
                    DocumentPreviewContentBudget.CheckDeclaredLength(output.PdfLength, snapshot.MaxOutputBytes, DocumentErrorCodes.OfficePreviewOutputTooLarge);
                    using var boundedOutput = new DocumentPreviewBudgetStream(output.PdfContent, snapshot.MaxOutputBytes, DocumentErrorCodes.OfficePreviewOutputTooLarge);
                    var uploadResult = await hostFileUploadWriter
                        .UploadAsync(
                            task.RequestedByUserId,
                            BuildOutputFileName(task),
                            "application/pdf",
                            boundedOutput,
                            output.PdfLength,
                            executionToken)
                        .ConfigureAwait(false);
                    if (!uploadResult.IsSuccess)
                    {
                        await MarkFailedAsync(task, uploadResult.Error!.Code, executionToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    EnsureActiveLease(task, executionToken);
                    await CompleteAsync(task, uploadResult.Value!.FileId, null, executionToken).ConfigureAwait(false);
                }
                finally
                {
                    await output.PdfContent.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await source.Content.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停机保留取消语义，不能用已取消令牌把它改写为转换失败。
            throw;
        }
        catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested || !IsLeaseActive(task))
        {
            // 过期结果不再上传或发布，任务由有界收口 SQL 转为租约失效终态。
        }
        catch (DocumentPreviewContentLimitException exception)
        {
            await MarkFailedAsync(task, exception.Code, executionToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await MarkFailedAsync(
                task,
                DocumentErrorCodes.OfficePreviewConversionFailed,
                executionToken).ConfigureAwait(false);
        }
        finally
        {
            if (workingDirectory is not null && Directory.Exists(workingDirectory))
            {
                try
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
                catch
                {
                    // 临时目录清理失败不阻断后续任务。
                }
            }
        }
    }

    private Task<bool> MarkFailedAsync(
        DocumentPreviewTaskRecord task,
        string errorCode,
        CancellationToken cancellationToken) => CompleteAsync(task, null, errorCode, cancellationToken);

    private async Task<bool> CompleteAsync(DocumentPreviewTaskRecord task, Guid? outputFileId, string? errorCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLeaseActive(task)) return false;
        return await transaction.ExecuteAsync(async token =>
        {
            var locked = await queryExecutor.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                    ? DocumentPreviewTaskSql.LockCompletionSqlServer : DocumentPreviewTaskSql.LockCompletionMySql,
                DocumentSqlParameters.Create(("Id", task.Id)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // 所有文件和转换调用均已结束；取得行锁后重新取时间，事务只仲裁本模块主行。
            var now = clock.UtcNow;
            if (locked is null || locked.StatusKey != HostDocumentPreviewTaskStatusKeys.Processing
                || locked.LeaseId != task.LeaseId || locked.LeaseExpiresAtUtc is null || locked.LeaseExpiresAtUtc <= now) return false;
            var affected = await commandExecutor.ExecuteAsync(errorCode is null ? DocumentPreviewTaskSql.MarkSucceeded : DocumentPreviewTaskSql.MarkFailed,
                DocumentSqlParameters.Create(("Id", task.Id), ("LeaseId", task.LeaseId), ("Now", now),
                    ("OutputFileId", outputFileId), ("ErrorCode", errorCode), ("CompletedAtUtc", now)), token).ConfigureAwait(false);
            if (affected > 1) throw new InvalidOperationException("Preview completion changed more than one task.");
            return affected == 1;
        }, cancellationToken).ConfigureAwait(false);
    }

    private bool IsLeaseActive(DocumentPreviewTaskRecord task) =>
        task.LeaseId is Guid id && id != Guid.Empty && task.LeaseExpiresAtUtc > clock.UtcNow;

    private void EnsureActiveLease(DocumentPreviewTaskRecord task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsLeaseActive(task)) throw new OperationCanceledException("Preview processing lease expired.");
    }

    private async Task ExpireProcessingAsync(DocumentOfficePreviewConversionOptions snapshot, int batchSize, CancellationToken token)
    {
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? DocumentPreviewTaskSql.ExpireProcessingSqlServer : DocumentPreviewTaskSql.ExpireProcessingMySql,
            DocumentSqlParameters.Create(("BatchSize", batchSize), ("Now", now),
                ("LegacyCutoff", now.AddSeconds(-snapshot.EffectiveLeaseSeconds)), ("ErrorCode", DocumentErrorCodes.OfficePreviewLeaseExpired)), token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DocumentPreviewTaskRecord>> ClaimAsync(
        int batchSize,
        int leaseSeconds,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var leaseId = Guid.CreateVersion7();
        var expires = now.AddSeconds(leaseSeconds);
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var rows = await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(
                    DocumentPreviewTaskSql.ClaimPendingSqlServer,
                    DocumentSqlParameters.Create(("BatchSize", batchSize), ("Now", now), ("LeaseId", leaseId), ("LeaseExpiresAtUtc", expires)),
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateClaim(rows, batchSize, leaseId);
            return rows;
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            DocumentPreviewTaskSql.SelectClaimableIdsMySql,
                            DocumentSqlParameters.Create(("BatchSize", batchSize)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return Array.Empty<DocumentPreviewTaskRecord>();
                    }
                    if (ids.Count > batchSize) throw new InvalidOperationException("Preview claim exceeded its configured batch.");

                    var affected = await commandExecutor.ExecuteAsync(
                            DocumentPreviewTaskSql.ClaimByIdsMySql,
                            DocumentSqlParameters.Create(("Ids", ids.ToArray()), ("Now", now), ("LeaseId", leaseId), ("LeaseExpiresAtUtc", expires)),
                            token)
                        .ConfigureAwait(false);
                    // 候选行由同一事务锁住；仲裁不完整时回滚，不能把其他实例的 processing 行当作本实例结果。
                    if (affected != ids.Count) throw new InvalidOperationException("Document preview claim changed before transition.");
                    var claimed = await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(
                            DocumentPreviewTaskSql.SelectByIds,
                            DocumentSqlParameters.Create(("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                    if (claimed.Count != ids.Count)
                        throw new InvalidOperationException("Document preview claim reload did not preserve ownership.");
                    ValidateClaim(claimed, batchSize, leaseId);
                    return claimed;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateClaim(IReadOnlyList<DocumentPreviewTaskRecord> rows, int batchSize, Guid leaseId)
    {
        if (rows.Count > batchSize || rows.Any(task => task.StatusKey != HostDocumentPreviewTaskStatusKeys.Processing
                || task.LeaseId != leaseId || task.LeaseExpiresAtUtc is null))
            throw new InvalidOperationException("Preview claim did not preserve its processing lease.");
    }

    private static string CreateWorkingDirectory(DocumentOfficePreviewConversionOptions options)
    {
        var root = string.IsNullOrWhiteSpace(options.TempRootPath)
            ? Path.Combine(Path.GetTempPath(), "fullnet-document-preview")
            : options.TempRootPath;
        Directory.CreateDirectory(root);
        return Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
    }

    private static string BuildOutputFileName(DocumentPreviewTaskRecord task)
    {
        var baseName = string.IsNullOrWhiteSpace(task.SourceFileName)
            ? task.DocumentTitle
            : Path.GetFileNameWithoutExtension(task.SourceFileName);
        return $"{baseName}-preview.pdf";
    }
}
