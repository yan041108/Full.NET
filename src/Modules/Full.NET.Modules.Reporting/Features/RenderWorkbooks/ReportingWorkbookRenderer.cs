using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.Modules.Reporting.Features.RenderWorkbooks;

/// <summary>复用正式报表的预算和公式转义，业务模块不能用新渲染入口绕开输出边界。</summary>
internal sealed class ReportingWorkbookRenderer : IReportingWorkbookRenderer
{
    public Result<byte[]> Render(IReadOnlyList<ReportingExecutionColumnDefinition> columns,
        IReadOnlyList<ReportingExecutionRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        cancellationToken.ThrowIfCancellationRequested();
        if (rows.Count > ReportingExportPolicy.MaxExportRows || columns.Count == 0 || columns.Count > 256 || columns.Any(c => string.IsNullOrWhiteSpace(c.ColumnKey))
            || columns.Select(c => c.ColumnKey).Distinct(StringComparer.Ordinal).Count() != columns.Count)
            return Invalid();
        var keys = columns.Select(c => c.ColumnKey).ToHashSet(StringComparer.Ordinal);
        if (rows.Any(r => r.Values.Keys.Any(k => !keys.Contains(k)))) return Invalid();
        try { return Result<byte[]>.Success(ReportingExcelExportRenderer.Render(columns, rows, cancellationToken)); }
        catch (InvalidDataException) { return Invalid(); }
    }
    private static Result<byte[]> Invalid() => Result<byte[]>.Failure(new Error(ReportingErrorCodes.ExportSizeLimitExceeded,
        "The workbook shape or resource budget is invalid.", ErrorType.Validation));
}
