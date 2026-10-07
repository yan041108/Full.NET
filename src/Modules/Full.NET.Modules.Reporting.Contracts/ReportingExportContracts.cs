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
/// <param name="VersionNumber">目标发布版本号；省略时 Host 使用最近发布版本，租户使用最近获授版本。</param>
/// <param name="Parameters">受控执行参数。</param>
public sealed record CreateReportingExportTaskRequest(
    Guid DefinitionId,
    string FormatKey,
    int? VersionNumber,
    IReadOnlyList<ReportingExecutionParameterValue> Parameters);

/// <summary>报表导出任务摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 FormatKey、StatusKey、ErrorCode 等稳定键发布后不可改名或删除。</remarks>
/// <param name="Id">导出任务标识。</param>
/// <param name="DefinitionId">目标报表定义标识。</param>
/// <param name="DefinitionKey">目标报表定义稳定键；前端据此路由。</param>
/// <param name="DefinitionName">目标报表定义名称（仅用于展示）。</param>
/// <param name="VersionNumber">实际使用的发布版本号。</param>
/// <param name="FormatKey">导出格式稳定键；当前仅支持 <see cref="ReportingExportFormatKeys.Excel"/>。</param>
/// <param name="StatusKey">任务状态稳定键；取值见 <see cref="ReportingExportTaskStatusKeys"/>。</param>
/// <param name="RowCount">导出行数；未生成或失败时为 0。</param>
/// <param name="OutputFileName">成功后的输出文件名；未完成时为空。</param>
/// <param name="ErrorCode">失败时返回的稳定错误码前缀；成功时为空。</param>
/// <param name="ErrorMessage">失败时的可读说明；不包含敏感数据。</param>
/// <param name="RequestedByUserId">发起人用户标识。</param>
/// <param name="CreatedAtUtc">任务创建时间（UTC）。</param>
/// <param name="CompletedAtUtc">任务完成时间（UTC）；未完成时为空。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 FormatKey、StatusKey、ErrorCode 等稳定键发布后不可改名或删除。</remarks>
/// <param name="Id">导出任务标识。</param>
/// <param name="DefinitionId">目标报表定义标识。</param>
/// <param name="DefinitionKey">目标报表定义稳定键；前端据此路由。</param>
/// <param name="DefinitionName">目标报表定义名称（仅用于展示）。</param>
/// <param name="VersionNumber">实际使用的发布版本号。</param>
/// <param name="FormatKey">导出格式稳定键；当前仅支持 <see cref="ReportingExportFormatKeys.Excel"/>。</param>
/// <param name="StatusKey">任务状态稳定键；取值见 <see cref="ReportingExportTaskStatusKeys"/>。</param>
/// <param name="RowCount">导出行数；未生成或失败时为 0。</param>
/// <param name="OutputFileId">输出文件标识；用于下载端点鉴权，未完成时为空。</param>
/// <param name="OutputFileName">成功后的输出文件名；未完成时为空。</param>
/// <param name="ErrorCode">失败时返回的稳定错误码前缀；成功时为空。</param>
/// <param name="ErrorMessage">失败时的可读说明；不包含敏感数据。</param>
/// <param name="Parameters">受控执行参数集合；与创建请求顺序一致。</param>
/// <param name="RequestedByUserId">发起人用户标识。</param>
/// <param name="CreatedAtUtc">任务创建时间（UTC）。</param>
/// <param name="CompletedAtUtc">任务完成时间（UTC）；未完成时为空。</param>
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
