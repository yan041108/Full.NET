namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>报表导出任务权限码。</summary>
public static class ReportingExportTaskPermissions
{
    /// <summary>创建报表导出任务。</summary>
    public const string Create = "reporting.export_tasks.create";

    /// <summary>读取报表导出任务列表与详情。</summary>
    public const string Read = "reporting.export_tasks.read";

    /// <summary>下载已完成的报表导出文件。</summary>
    public const string Download = "reporting.export_tasks.download";
}

/// <summary>报表导出格式键。</summary>
public static class ReportingExportFormatKeys
{
    /// <summary>Open XML Excel 工作簿。</summary>
    public const string Excel = "excel";
}

/// <summary>报表导出任务状态键。</summary>
public static class ReportingExportTaskStatusKeys
{
    /// <summary>已持久化导出意图，等待领取执行。</summary>
    public const string Queued = "queued";

    /// <summary>正在生成导出文件。</summary>
    public const string Processing = "processing";

    /// <summary>导出成功且可下载。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>导出失败。</summary>
    public const string Failed = "failed";
}

/// <summary>创建报表导出任务请求。</summary>
/// <param name="DefinitionId">目标报表定义标识。</param>
/// <param name="FormatKey">导出格式键，当前仅支持 <see cref="ReportingExportFormatKeys.Excel"/>。</param>
/// <param name="VersionNumber">目标发布版本号；省略时使用最近发布版本。</param>
/// <param name="Parameters">受控执行参数。</param>
public sealed record CreateReportingExportTaskRequest(
    Guid DefinitionId,
    string FormatKey,
    int? VersionNumber,
    IReadOnlyList<ReportingExecutionParameterValue> Parameters);

/// <summary>报表导出任务摘要。</summary>
public sealed record ReportingExportTaskResponse(
    Guid Id,
    Guid DefinitionId,
    string DefinitionKey,
    string DefinitionName,
    int VersionNumber,
    string FormatKey,
    string StatusKey,
    int RowCount,
    string? OutputFileName,
    string? ErrorCode,
    string? ErrorMessage,
    Guid RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>报表导出任务详情。</summary>
public sealed record ReportingExportTaskDetailResponse(
    Guid Id,
    Guid DefinitionId,
    string DefinitionKey,
    string DefinitionName,
    int VersionNumber,
    string FormatKey,
    string StatusKey,
    int RowCount,
    Guid? OutputFileId,
    string? OutputFileName,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<ReportingExecutionParameterValue> Parameters,
    Guid RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
