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
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
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
        string? workingDirectory = null;
        try
        {
            var sourceResult = await hostFileContentReader
                .OpenReadyContentAsync(task.SourceFileId, cancellationToken)
                .ConfigureAwait(false);
            if (!sourceResult.IsSuccess)
            {
                await MarkFailedAsync(task.Id, sourceResult.Error!.Code, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            var source = sourceResult.Value!;
            try
            {
                if (source.Content.CanSeek && source.Content.Length > options.CurrentValue.MaxInputBytes)
                {
                    await MarkFailedAsync(
                        task.Id,
                        DocumentErrorCodes.OfficePreviewInputTooLarge,
                        now,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                workingDirectory = CreateWorkingDirectory(options.CurrentValue);
                var provider = providerResolver.ResolveCurrent();
                var conversionResult = await provider
                    .ConvertAsync(
                        new DocumentOfficePreviewConversionInput(
                            source.OriginalFileName,
                            source.ContentType,
                            source.Content,
                            source.Content.CanSeek ? source.Content.Length : options.CurrentValue.MaxInputBytes),
                        workingDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!conversionResult.IsSuccess)
                {
                    await MarkFailedAsync(task.Id, conversionResult.Error!.Code, now, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var output = conversionResult.Value!;
                try
                {
                    var uploadResult = await hostFileUploadWriter
                        .UploadAsync(
                            task.RequestedByUserId,
                            BuildOutputFileName(task),
                            "application/pdf",
                            output.PdfContent,
                            output.PdfLength,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!uploadResult.IsSuccess)
                    {
                        await MarkFailedAsync(task.Id, uploadResult.Error!.Code, now, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    await commandExecutor.ExecuteAsync(
                            DocumentPreviewTaskSql.MarkSucceeded,
                            DocumentSqlParameters.Create(
                                ("Id", task.Id),
                                ("OutputFileId", uploadResult.Value!.FileId),
                                ("CompletedAtUtc", now)),
                            cancellationToken)
                        .ConfigureAwait(false);
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
        catch (Exception)
        {
            await MarkFailedAsync(
                task.Id,
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

    private async Task MarkFailedAsync(
        Guid taskId,
        string errorCode,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                DocumentPreviewTaskSql.MarkFailed,
                DocumentSqlParameters.Create(
                    ("Id", taskId),
                    ("ErrorCode", errorCode),
                    ("CompletedAtUtc", completedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<DocumentPreviewTaskRecord>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            return await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(
                    DocumentPreviewTaskSql.ClaimPendingSqlServer,
                    DocumentSqlParameters.Create(("BatchSize", batchSize), ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
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

                    await commandExecutor.ExecuteAsync(
                            DocumentPreviewTaskSql.ClaimByIdsMySql,
                            DocumentSqlParameters.Create(("Ids", ids.ToArray()), ("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    return await queryExecutor.QueryAsync<DocumentPreviewTaskRecord>(
                            DocumentPreviewTaskSql.SelectByIds,
                            DocumentSqlParameters.Create(("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
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
