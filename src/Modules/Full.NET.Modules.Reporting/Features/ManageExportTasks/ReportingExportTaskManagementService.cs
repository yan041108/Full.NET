using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>创建报表导出任务、同步执行查询并写入 Files。</summary>
internal sealed class ReportingExportTaskManagementService(
    ReportingDefinitionQueryService definitionQueries,
    ReportingDefinitionExecutionService executionService,
    IHostFileUploadWriter hostFileUploadWriter,
    IHostFileContentReader hostFileContentReader,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>创建导出任务并同步完成 Excel 生成。</summary>
    public async Task<Result<ReportingExportTaskDetailResponse>> CreateAsync(
        CreateReportingExportTaskRequest request,
        Guid requestedByUserId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var tenantId = currentTenant.Id!.Value;

        if (!string.Equals(request.FormatKey, ReportingExportFormatKeys.Excel, StringComparison.Ordinal))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(UnsupportedFormatError());
        }

        if (request.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.ParameterKey)))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError(
                "Export parameter keys are required."));
        }

        var definitionResult = await definitionQueries.GetByIdAsync(request.DefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (!definitionResult.IsSuccess || definitionResult.Value is null)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(definitionResult.Error!);
        }

        var definition = definitionResult.Value;
        if (!definition.IsEnabled)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError(
                "The reporting definition is disabled."));
        }

        var versionNumber = request.VersionNumber ?? definition.LatestPublishedVersionNumber;
        if (versionNumber <= 0)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                ReportingErrorCodes.DefinitionNotPublished,
                "The reporting definition has no published version to export.",
                ErrorType.Validation));
        }

        var taskId = idGenerator.NewId();
        var now = clock.UtcNow;
        var parametersJson = ReportingExportTaskMapper.SerializeParameters(request.Parameters);
        var record = new ReportingExportTaskRecord
        {
            Id = taskId,
            TenantId = tenantId,
            DefinitionId = definition.Id,
            VersionNumber = versionNumber,
            DefinitionKey = definition.DefinitionKey,
            DefinitionName = definition.Name,
            FormatKey = request.FormatKey,
            ParametersJson = parametersJson,
            StatusKey = ReportingExportTaskStatusKeys.Processing,
            RowCount = 0,
            RequestedByUserId = requestedByUserId,
            CreatedAtUtc = now,
            Version = 1,
        };

        await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);

        var executeRequest = new ExecuteReportingDefinitionRequest(versionNumber, request.Parameters);
        var collectOutcome = await CollectExportRowsAsync(
                definition.Id,
                executeRequest,
                principal,
                cancellationToken)
            .ConfigureAwait(false);
        if (!collectOutcome.IsSuccess)
        {
            await MarkFailedAsync(
                    taskId,
                    1,
                    collectOutcome.RowCount,
                    collectOutcome.ErrorCode!,
                    collectOutcome.ErrorMessage!,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                collectOutcome.ErrorCode!,
                collectOutcome.ErrorMessage!,
                ErrorType.Validation));
        }

        var fileName = BuildFileName(definition.DefinitionKey, versionNumber, now);
        var workbookBytes = ReportingExcelExportRenderer.Render(
            collectOutcome.Columns!,
            collectOutcome.Rows!);
        if (workbookBytes.LongLength > ReportingExportPolicy.MaxExportBytes)
        {
            await MarkFailedAsync(
                    taskId,
                    1,
                    collectOutcome.RowCount,
                    ReportingErrorCodes.ExportSizeLimitExceeded,
                    "The generated export file exceeds the maximum allowed size.",
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                ReportingErrorCodes.ExportSizeLimitExceeded,
                "The generated export file exceeds the maximum allowed size.",
                ErrorType.Validation));
        }

        await using var uploadStream = new MemoryStream(workbookBytes, writable: false);
        var uploadResult = await hostFileUploadWriter
            .UploadAsync(
                requestedByUserId,
                fileName,
                WorkbookContentType,
                uploadStream,
                workbookBytes.LongLength,
                cancellationToken)
            .ConfigureAwait(false);
        if (!uploadResult.IsSuccess)
        {
            await MarkFailedAsync(
                    taskId,
                    1,
                    collectOutcome.RowCount,
                    ReportingErrorCodes.ExportFailed,
                    uploadResult.Error?.Message ?? "Failed to store export file.",
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<ReportingExportTaskDetailResponse>.Failure(uploadResult.Error!);
        }

        var completedAt = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.CompleteSucceeded,
                ReportingSqlParameters.Create([
                    ("Id", taskId),
                    ("StatusKey", ReportingExportTaskStatusKeys.Succeeded),
                    ("OutputFileId", uploadResult.Value!.FileId),
                    ("OutputFileName", fileName),
                    ("RowCount", collectOutcome.RowCount),
                    ("CompletedAtUtc", completedAt),
                    ("Version", 1L)]),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                ReportingErrorCodes.ExportFailed,
                "The export task completion update failed due to a concurrency conflict.",
                ErrorType.Conflict));
        }

        var detail = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindById,
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return detail is null
            ? Result<ReportingExportTaskDetailResponse>.Failure(TaskNotFoundError())
            : Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(detail));
    }

    /// <summary>打开已完成导出任务的文件内容流。</summary>
    public async Task<Result<HostFileContent>> OpenDownloadAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindById,
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<HostFileContent>.Failure(TaskNotFoundError());
        }

        if (!string.Equals(record.StatusKey, ReportingExportTaskStatusKeys.Succeeded, StringComparison.Ordinal)
            || record.OutputFileId is null)
        {
            return Result<HostFileContent>.Failure(new Error(
                ReportingErrorCodes.ExportTaskNotReady,
                "The reporting export task is not ready for download.",
                ErrorType.Validation));
        }

        var content = await hostFileContentReader
            .OpenReadyContentAsync(record.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!content.IsSuccess)
        {
            return Result<HostFileContent>.Failure(content.Error!);
        }

        var file = content.Value!;
        return Result<HostFileContent>.Success(new HostFileContent(
            file.Content,
            file.ContentType ?? WorkbookContentType,
            record.OutputFileName ?? file.OriginalFileName));
    }

    private async Task<ExportCollectOutcome> CollectExportRowsAsync(
        Guid definitionId,
        ExecuteReportingDefinitionRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var allRows = new List<ReportingExecutionRow>();
        IReadOnlyList<ReportingExecutionColumnDefinition>? columns = null;
        var page = 1;
        while (allRows.Count < ReportingExportPolicy.MaxExportRows)
        {
            var pageResult = await executionService.ExecuteAsync(
                    definitionId,
                    request,
                    page,
                    ReportingExportPolicy.FetchPageSize,
                    principal,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!pageResult.IsSuccess || pageResult.Value is null)
            {
                return ExportCollectOutcome.Failed(
                    pageResult.Error?.Code ?? ReportingErrorCodes.ExportFailed,
                    pageResult.Error?.Message ?? "Reporting export query failed.",
                    allRows.Count);
            }

            var pageValue = pageResult.Value;
            columns = pageValue.Columns;
            foreach (var row in pageValue.Rows)
            {
                if (allRows.Count >= ReportingExportPolicy.MaxExportRows)
                {
                    return ExportCollectOutcome.Failed(
                        ReportingErrorCodes.ExportRowLimitExceeded,
                        $"The export exceeds the maximum of {ReportingExportPolicy.MaxExportRows} rows.",
                        allRows.Count);
                }

                allRows.Add(row);
            }

            if (!pageValue.HasMore)
            {
                break;
            }

            page++;
        }

        if (columns is null || columns.Count == 0)
        {
            return ExportCollectOutcome.Failed(
                ReportingErrorCodes.ExecutionColumnsDenied,
                "No result columns are visible for the current principal.",
                allRows.Count);
        }

        return ExportCollectOutcome.Succeeded(columns, allRows);
    }

    private async Task MarkFailedAsync(
        Guid taskId,
        long version,
        int rowCount,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.CompleteFailed,
                ReportingSqlParameters.Create(
                    ("Id", taskId),
                    ("StatusKey", ReportingExportTaskStatusKeys.Failed),
                    ("RowCount", rowCount),
                    ("ErrorCode", errorCode),
                    ("ErrorMessage", errorMessage),
                    ("CompletedAtUtc", clock.UtcNow),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildFileName(string definitionKey, int versionNumber, DateTimeOffset timestamp) =>
        $"{definitionKey}-v{versionNumber}-{timestamp:yyyyMMddHHmmss}.xlsx";

    private void EnsureTenantContext()
    {
        if (currentTenant.Id is null)
        {
            throw new InvalidOperationException("Tenant context is required for reporting export tasks.");
        }
    }

    private static Error UnsupportedFormatError() =>
        new(ReportingErrorCodes.ExportFormatUnsupported, "The export format is not supported.", ErrorType.Validation);

    private static Error ExportFailedError(string message) =>
        new(ReportingErrorCodes.ExportFailed, message, ErrorType.Validation);

    private static Error TaskNotFoundError() =>
        new(ReportingErrorCodes.ExportTaskNotFound, "The reporting export task was not found.", ErrorType.NotFound);

    private sealed class ExportCollectOutcome
    {
        public bool IsSuccess { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public int RowCount { get; init; }
        public IReadOnlyList<ReportingExecutionColumnDefinition>? Columns { get; init; }
        public IReadOnlyList<ReportingExecutionRow>? Rows { get; init; }

        public static ExportCollectOutcome Succeeded(
            IReadOnlyList<ReportingExecutionColumnDefinition> columns,
            IReadOnlyList<ReportingExecutionRow> rows) =>
            new()
            {
                IsSuccess = true,
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
            };

        public static ExportCollectOutcome Failed(string errorCode, string errorMessage, int rowCount) =>
            new()
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                RowCount = rowCount,
            };
    }
}
