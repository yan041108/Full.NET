using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>已生成且尚未绑定任务的导出工作簿。</summary>
/// <param name="RowCount">导出行数。</param>
/// <param name="FileName">安全下载文件名。</param>
/// <param name="Content">xlsx 字节。</param>
internal sealed record ReportingExportGeneratedFile(int RowCount, string FileName, byte[] Content);

/// <summary>按任务快照收集并渲染导出文件；实现必须在事务外执行外部查询。</summary>
internal interface IReportingExportWorkbookSource
{
    /// <summary>收集报表行并渲染工作簿。</summary>
    /// <param name="task">已领取的导出任务。</param>
    /// <param name="principal">列权限主体；Worker 恢复时使用创建时快照重建。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<ReportingExportGeneratedFile>> GenerateAsync(
        ReportingExportTaskRecord task,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

/// <summary>使用定义查询与执行服务生成 Excel 工作簿。</summary>
/// <param name="executionService">受控查询执行。</param>
internal sealed class ReportingExportWorkbookSource(
    ReportingDefinitionExecutionService executionService) : IReportingExportWorkbookSource
{
    private const string WorkbookContentTypeHint = "xlsx";

    /// <inheritdoc />
    public async Task<Result<ReportingExportGeneratedFile>> GenerateAsync(
        ReportingExportTaskRecord task,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var executeRequest = new ExecuteReportingDefinitionRequest(
            task.VersionNumber,
            ReportingExportTaskMapper.DeserializeParameters(task.ParametersJson));
        var collectOutcome = await CollectExportRowsAsync(
                task.DefinitionId,
                executeRequest,
                principal,
                cancellationToken)
            .ConfigureAwait(false);
        if (!collectOutcome.IsSuccess)
        {
            return Result<ReportingExportGeneratedFile>.Failure(new Error(
                collectOutcome.ErrorCode!,
                collectOutcome.ErrorMessage!,
                ErrorType.Validation));
        }

        byte[] workbookBytes;
        try
        {
            workbookBytes = ReportingExcelExportRenderer.Render(collectOutcome.Columns!, collectOutcome.Rows!);
        }
        catch (InvalidDataException)
        {
            return Result<ReportingExportGeneratedFile>.Failure(new Error(
                ReportingErrorCodes.ExportSizeLimitExceeded,
                "The export exceeds the input or output size limit.",
                ErrorType.Validation));
        }

        var fileName = $"{task.DefinitionKey}-v{task.VersionNumber}-{task.CreatedAtUtc:yyyyMMddHHmmss}.{WorkbookContentTypeHint}";
        return Result<ReportingExportGeneratedFile>.Success(
            new ReportingExportGeneratedFile(collectOutcome.RowCount, fileName, workbookBytes));
    }

    /// <summary>按页收集导出行，并在超限时立即失败。</summary>
    /// <param name="definitionId">报表定义。</param>
    /// <param name="request">执行请求。</param>
    /// <param name="principal">列权限主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<ExportCollectOutcome> CollectExportRowsAsync(
        Guid definitionId,
        ExecuteReportingDefinitionRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var allRows = new List<ReportingExecutionRow>();
        var budget = new ReportingExportBudget();
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
            try
            {
                if (columns is null)
                {
                    budget.AddColumns(pageValue.Columns);
                }

                foreach (var row in pageValue.Rows)
                {
                    budget.AddRow(row);
                }
            }
            catch (InvalidDataException)
            {
                return ExportCollectOutcome.Failed(
                    ReportingErrorCodes.ExportSizeLimitExceeded,
                    "The export input memory limit was exceeded.",
                    allRows.Count);
            }

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

            if (allRows.Count >= ReportingExportPolicy.MaxExportRows)
            {
                return ExportCollectOutcome.Failed(
                    ReportingErrorCodes.ExportRowLimitExceeded,
                    $"The export exceeds the maximum of {ReportingExportPolicy.MaxExportRows} rows.",
                    allRows.Count);
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
