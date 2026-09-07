using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>创建导入任务、上传源文件并同步执行预校验。</summary>
internal sealed class ImportExportTaskManagementService(
    StaticImportSchemaRegistry registry,
    ITenantResourceFileStore resourceFiles,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptionsMonitor<ImportExportOptions> options)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<Result<ImportExportTaskDetailResponse>> CreateAsync(
        string schemaKey,
        string worksheetKey,
        string originalFileName,
        Stream content,
        long contentLength,
        Guid requestedByUserId,
        StaticImportPreviewContext previewContext,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var tenantId = currentTenant.Id!.Value;

        if (contentLength <= 0 || contentLength > options.CurrentValue.MaxUploadBytes)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(FileTooLargeError());
        }

        if (!string.Equals(Path.GetExtension(originalFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ImportExportTaskDetailResponse>.Failure(FileInvalidError());
        }

        var handler = registry.TryResolve(schemaKey);
        if (handler is null)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(SchemaNotFoundError());
        }

        var definition = handler.GetDefinition();
        if (!definition.Worksheets.Any(worksheet =>
                string.Equals(worksheet.WorksheetKey, worksheetKey, StringComparison.Ordinal)))
        {
            return Result<ImportExportTaskDetailResponse>.Failure(WorksheetNotFoundError());
        }

        var taskId = idGenerator.NewId();
        var upload = await resourceFiles
            .UploadAsync(
                "import_export", taskId, requestedByUserId,
                originalFileName,
                WorkbookContentType,
                content,
                contentLength,
                cancellationToken)
            .ConfigureAwait(false);
        if (!upload.IsSuccess)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(upload.Error!);
        }

        var uploaded = upload.Value!;
        var openContent = await resourceFiles
            .OpenReadyContentAsync("import_export", taskId, uploaded.FileId, cancellationToken)
            .ConfigureAwait(false);
        if (!openContent.IsSuccess)
        {
            return Result<ImportExportTaskDetailResponse>.Failure(openContent.Error!);
        }

        var probe = openContent.Value!;
        Result<StaticImportPreviewResult> previewResult;
        try
        {
            previewResult = await handler
                .PreviewAsync(
                    probe.Content,
                    contentLength,
                    previewContext,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            previewResult = Result<StaticImportPreviewResult>.Failure(FileInvalidError());
        }
        finally
        {
            await probe.Content.DisposeAsync().ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        string statusKey;
        int totalRows;
        int validRowCount;
        int invalidRowCount;
        string? previewRowsJson;
        string? errorCode;

        if (!previewResult.IsSuccess)
        {
            statusKey = ImportExportTaskStatusKeys.PreviewFailed;
            totalRows = 0;
            validRowCount = 0;
            invalidRowCount = 0;
            previewRowsJson = null;
            errorCode = previewResult.Error?.Code ?? ImportExportErrorCodes.PreviewFailed;
        }
        else
        {
            var preview = previewResult.Value!;
            if (preview.TotalRows > options.CurrentValue.MaxPreviewRows)
            {
                return Result<ImportExportTaskDetailResponse>.Failure(RowLimitExceededError());
            }

            totalRows = preview.TotalRows;
            validRowCount = preview.ValidRowCount;
            invalidRowCount = preview.InvalidRowCount;
            previewRowsJson = ImportExportTaskMapper.SerializePreviewRows(preview.Rows);
            errorCode = null;
            statusKey = invalidRowCount == 0
                ? ImportExportTaskStatusKeys.PreviewSucceeded
                : ImportExportTaskStatusKeys.PreviewFailed;
        }

        await commandExecutor.ExecuteAsync(
                ImportExportTaskSql.Insert,
                ImportExportSqlParameters.Create(
                    ("Id", taskId),
                    ("TenantId", tenantId),
                    ("SchemaKey", definition.SchemaKey),
                    ("SchemaDisplayName", definition.DisplayName),
                    ("WorksheetKey", worksheetKey),
                    ("SourceFileId", uploaded.FileId),
                    ("SourceFileName", originalFileName),
                    ("StatusKey", statusKey),
                    ("TotalRows", totalRows),
                    ("ValidRowCount", validRowCount),
                    ("InvalidRowCount", invalidRowCount),
                    ("PreviewRowsJson", previewRowsJson),
                    ("ErrorCode", errorCode),
                    ("RequestedByUserId", requestedByUserId),
                    ("CreatedAtUtc", now),
                    ("PreviewCompletedAtUtc", now),
                    ("ProcessedRowCount", 0),
                    ("SucceededRowCount", 0),
                    ("ExecutionFailedRowCount", 0),
                    ("NextLineNumber", 0),
                    ("ExecutionRowsJson", null),
                    ("ErrorReceiptFileId", null),
                    ("ExecutionStartedAtUtc", null),
                    ("ExecutionCompletedAtUtc", null),
                    ("LeaseId", null),
                    ("LeaseExpiresAtUtc", null),
                    ("Version", 1L)),
                cancellationToken)
            .ConfigureAwait(false);

        var created = await queryExecutor
            .QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                ImportExportTaskSql.FindById,
                ImportExportSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return created is null
            ? Result<ImportExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ImportExportTaskDetailResponse>.Success(ImportExportTaskMapper.MapDetail(created));
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("import_export.tenant_context_required");
        }
    }

    private static Error SchemaNotFoundError() =>
        new(ImportExportErrorCodes.SchemaNotFound, "The static import schema was not found.", ErrorType.NotFound);

    private static Error WorksheetNotFoundError() =>
        new(ImportExportErrorCodes.WorksheetNotFound, "The static import worksheet was not found.", ErrorType.NotFound);

    private static Error TaskNotFoundError() =>
        new(ImportExportErrorCodes.TaskNotFound, "The import task was not found.", ErrorType.NotFound);

    private static Error FileInvalidError() =>
        new(ImportExportErrorCodes.FileInvalid, "The import file is invalid.", ErrorType.Validation);

    private static Error FileTooLargeError() =>
        new(ImportExportErrorCodes.FileTooLarge, "The import file exceeds the configured size limit.", ErrorType.BusinessRule);

    private static Error RowLimitExceededError() =>
        new(ImportExportErrorCodes.RowLimitExceeded, "The import file exceeds the configured row limit.", ErrorType.BusinessRule);
}
