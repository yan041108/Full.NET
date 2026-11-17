using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
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
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options, DocumentPreviewLifecycle lifecycle, IIdGenerator idGenerator)
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        await lifecycle.MaintainAsync(cancellationToken).ConfigureAwait(false);
        var currentOptions = options.CurrentValue;
        if (!currentOptions.CanConvert)
        {
            return 0;
        }

        var batchSize = Math.Clamp(currentOptions.BatchSize, 1, 50);
        var claimed = await ClaimAsync(batchSize, cancellationToken).ConfigureAwait(false);
        foreach (var task in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOneAsync(task, cancellationToken).ConfigureAwait(false);
        }

        return claimed.Count;
    }

    private async Task ProcessOneAsync(
        DocumentPreviewTaskRecord task,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var timeoutSeconds = options.CurrentValue.TimeoutSeconds;
        if (!await lifecycle.RenewAsync(task, now, timeoutSeconds, cancellationToken).ConfigureAwait(false)) return;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var maxAttempts = options.CurrentValue.MaxAttempts;
        var attemptRecorded = false;
        string? workingDirectory = null;
        try
        {
            var outputReference = (await lifecycle.ReferencesAsync(task.Id, attempt.Token).ConfigureAwait(false)).Single(r => r.Purpose == "output");
            if (outputReference.StateKey is "released" or "release_pending") return;
            var operationKey = DocumentPreviewLifecycle.UploadKey(outputReference.Id);
            // 先恢复原上传收据；ready 无需再转换，pending/未知结果不允许更换身份盲目上传。
            var recovered = await hostFileUploadWriter.FindUploadAsync(operationKey, task.OutputOwnerId!.Value, attempt.Token).ConfigureAwait(false);
            if (!recovered.IsSuccess)
            {
                await lifecycle.FailAsync(task, recovered.Error!.Code, false, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (recovered.Value is { } receipt)
            {
                if (receipt.FileId != outputReference.FileId) throw new InvalidOperationException("Preview upload identity mismatch.");
                await lifecycle.CompleteAsync(task, receipt.FileId, attempt.Token).ConfigureAwait(false);
                return;
            }
            // 即使转换次数已经用尽，也先尝试绑定上一轮持久化的 PDF 收据。
            if (task.AttemptCount >= maxAttempts)
            {
                await lifecycle.FailAsync(task, "document.preview.attempts_exhausted", false, attempt.Token).ConfigureAwait(false);
                return;
            }
            var sourceResult = await hostFileContentReader
                .OpenReadyContentAsync(task.SourceFileId, attempt.Token)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                await MarkFailedAsync(task, sourceResult.Error!.Code, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            var source = sourceResult.Value!;
            try
            {
                if (source.Content.CanSeek && source.Content.Length > options.CurrentValue.MaxInputBytes)
                {
                    await MarkFailedAsync(
                        task,
                        DocumentErrorCodes.OfficePreviewInputTooLarge,
                        now,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                workingDirectory = CreateWorkingDirectory(options.CurrentValue);
                var provider = providerResolver.ResolveCurrent();
                attemptRecorded = await lifecycle.TryBeginAttemptAsync(task, idGenerator.NewId(), maxAttempts, attempt.Token).ConfigureAwait(false);
                if (!attemptRecorded) return;
                var conversionResult = await provider
                    .ConvertAsync(
                        new DocumentOfficePreviewConversionInput(
                            source.OriginalFileName,
                            source.ContentType,
                            source.Content,
                            source.Content.CanSeek ? source.Content.Length : options.CurrentValue.MaxInputBytes),
                        workingDirectory,
                        attempt.Token)
                    .ConfigureAwait(false);
                if (!conversionResult.IsSuccess)
                {
                    await MarkFailedAsync(task, conversionResult.Error!.Code, now, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var output = conversionResult.Value!;
                try
                {
                    // 输出所有者、文件 UUID 与操作键在创建时固定，崩溃重领不生成第二份 PDF。
                    var uploadResult = await hostFileUploadWriter.UploadIdempotentAsync(operationKey,
                        task.OutputOwnerId.Value, BuildOutputFileName(task), "application/pdf", output.PdfContent,
                        output.PdfLength, attempt.Token, outputReference.FileId).ConfigureAwait(false);
                    if (!uploadResult.IsSuccess)
                    {
                        await MarkFailedAsync(task, uploadResult.Error!.Code, now, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    var committed = await lifecycle.CompleteAsync(task, uploadResult.Value!.FileId, attempt.Token).ConfigureAwait(false);
                    if (!committed) await lifecycle.FailAsync(task, "document.preview.result_not_committed", false, cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not ServiceCapacityExceededException)
        {
            // 其他准备异常也占用原有失败预算，避免持久错误在次数为零时反复重试。
            if (!attemptRecorded && task.AttemptCount < maxAttempts
                && !await lifecycle.TryBeginAttemptAsync(task, idGenerator.NewId(), maxAttempts, cancellationToken).ConfigureAwait(false)) return;
            await MarkFailedAsync(
                task,
                DocumentErrorCodes.OfficePreviewConversionFailed,
                now,
                cancellationToken).ConfigureAwait(false);
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

    private Task MarkFailedAsync(DocumentPreviewTaskRecord task, string errorCode, DateTimeOffset completedAtUtc, CancellationToken cancellationToken) =>
        lifecycle.FailAsync(task, errorCode, errorCode == DocumentErrorCodes.OfficePreviewConversionFailed, cancellationToken);

    internal async Task<IReadOnlyList<DocumentPreviewTaskRecord>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var parameters = DocumentSqlParameters.Create(("BatchSize", batchSize), ("Now", now),
            ("ExpiredBefore", now.AddSeconds(-LeaseSeconds)), ("LeaseExpiresAtUtc", now.AddSeconds(LeaseSeconds)));
        return await transaction.ExecuteAsync(async token =>
        {
            IReadOnlyList<DocumentPreviewTaskRecord> tasks;
            if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
                tasks = await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.ClaimPendingSqlServer, parameters, token).ConfigureAwait(false);
            else
            {
                var ids = await queryExecutor.QueryAsync<Guid>(DocumentPreviewTaskSql.SelectClaimableIdsMySql, parameters, token).ConfigureAwait(false);
                if (ids.Count == 0) return Array.Empty<DocumentPreviewTaskRecord>();
                await commandExecutor.ExecuteAsync(DocumentPreviewTaskSql.ClaimByIdsMySql,
                    DocumentSqlParameters.Create(("Ids", ids.ToArray()), ("Now", now), ("ExpiredBefore", now.AddSeconds(-LeaseSeconds)), ("LeaseExpiresAtUtc", now.AddSeconds(LeaseSeconds))), token).ConfigureAwait(false);
                tasks = await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.SelectByIds, DocumentSqlParameters.Create(("Ids", ids.ToArray())), token).ConfigureAwait(false);
            }
            foreach (var claimed in tasks)
            {
                await commandExecutor.ExecuteAsync(DocumentPreviewTaskSql.ExpirePreviousAttempts, DocumentSqlParameters.Create(("Id", claimed.Id), ("Version", claimed.Version), ("Now", now)), token).ConfigureAwait(false);
            }
            return tasks;
        }, cancellationToken).ConfigureAwait(false);
    }

    // 覆盖整个读源、转换和上传预算，并为保存终态保留余量。
    private int LeaseSeconds => lifecycle.LeaseSeconds;

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
