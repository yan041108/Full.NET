namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>访问日志受控导出权限。</summary>
public static class AccessLogExportPermissions
{
    /// <summary>在保留策略与时间/行数边界内导出访问日志。</summary>
    public const string Export = "auditing.access.export";
}

/// <summary>操作日志受控导出权限。</summary>
public static class OperationLogExportPermissions
{
    /// <summary>在保留策略与时间/行数边界内导出操作日志。</summary>
    public const string Export = "auditing.operations.export";
}

/// <summary>异常日志受控导出权限。</summary>
public static class ExceptionLogExportPermissions
{
    /// <summary>在保留策略与时间/行数边界内导出异常日志。</summary>
    public const string Export = "auditing.exceptions.export";
}

/// <summary>审计日志导出敏感字段权限。</summary>
public static class AuditLogExportPermissions
{
    /// <summary>导出 TraceId、客户端指纹、堆栈等敏感列。</summary>
    public const string SensitiveFields = "auditing.export.sensitive_fields";
}

/// <summary>审计日志导出请求体。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。空值表示不按该条件过滤。</remarks>
/// <param name="FromUtc">导出时间窗起点（UTC，含）。</param>
/// <param name="ToUtc">导出时间窗终点（UTC，含）。</param>
/// <param name="HttpMethod">按 HTTP 方法过滤；为空表示不过滤。</param>
/// <param name="StatusCode">按 HTTP 状态码过滤；为空表示不过滤。</param>
/// <param name="Succeeded">按是否成功过滤；为空表示不过滤。</param>
/// <param name="PathContains">按请求路径包含子串过滤；为空表示不过滤。</param>
/// <param name="ExceptionTypeContains">按异常类型名包含子串过滤；为空表示不过滤。</param>
public sealed record AuditLogExportRequest(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? HttpMethod = null,
    int? StatusCode = null,
    bool? Succeeded = null,
    string? PathContains = null,
    string? ExceptionTypeContains = null);

/// <summary>审计日志导出元数据响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RowCount">实际导出行数；受保留策略与行数上限约束。</param>
/// <param name="Truncated">是否因触达上限而被截断；为 <see langword="true"/> 时结果不完整。</param>
/// <param name="IncludesSensitiveFields">是否包含敏感列（TraceId、客户端指纹、堆栈等）。</param>
/// <param name="FromUtc">实际生效的导出起点（UTC，含）。</param>
/// <param name="ToUtc">实际生效的导出终点（UTC，含）。</param>
/// <param name="FileName">导出文件建议文件名。</param>
public sealed record AuditLogExportMetadataResponse(
    int RowCount,
    bool Truncated,
    bool IncludesSensitiveFields,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FileName);
