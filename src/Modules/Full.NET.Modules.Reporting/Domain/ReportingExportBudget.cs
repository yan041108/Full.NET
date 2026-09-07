using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>在保留报表行之前检查文本、列数和行数预算，避免压缩掩盖内存消耗。</summary>
internal sealed class ReportingExportBudget
{
    private long textBytes;
    private int rowCount;

    /// <summary>计入列定义，限制每行的对象数量。</summary>
    /// <param name="columns">当前可见列。</param>
    internal void AddColumns(IReadOnlyList<ReportingExecutionColumnDefinition> columns)
    {
        if (columns.Count > 256) throw new InvalidDataException("The export column limit was exceeded.");
        foreach (var column in columns)
        {
            AddText(column.ColumnKey);
            AddText(column.DisplayName);
        }
    }

    /// <summary>在收集或渲染之前计入一行，任何越界都失败关闭。</summary>
    /// <param name="row">即将保留的数据行。</param>
    internal void AddRow(ReportingExecutionRow row)
    {
        if (++rowCount > ReportingExportPolicy.MaxExportRows || row.Values.Count > 256)
            throw new InvalidDataException("The export row or column limit was exceeded.");
        foreach (var pair in row.Values)
        {
            AddText(pair.Key);
            AddText(pair.Value);
        }
    }

    /// <summary>以 UTF-16 字节计量当前字符串，预算同时约束高压缩比文本。</summary>
    /// <param name="value">可能为空的文本。</param>
    private void AddText(string? value)
    {
        textBytes += (long)(value?.Length ?? 0) * sizeof(char);
        if (textBytes > ReportingExportPolicy.MaxInputTextBytes)
            throw new InvalidDataException("The export input memory limit was exceeded.");
    }
}
