namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表导出背压与大小边界；导出必须受此约束。</summary>
internal static class ReportingExportPolicy
{
    /// <summary>单次导出允许的最大行数。</summary>
    public const int MaxExportRows = 5000;

    /// <summary>导出文件最大字节数（10 MiB）。</summary>
    public const long MaxExportBytes = 10 * 1024 * 1024;

    /// <summary>分页拉取导出数据时使用的页大小。</summary>
    public const int FetchPageSize = 200;
}
