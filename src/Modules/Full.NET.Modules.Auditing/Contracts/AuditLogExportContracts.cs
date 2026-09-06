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
public sealed record AuditLogExportRequest(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? HttpMethod = null,
    int? StatusCode = null,
    bool? Succeeded = null,
    string? PathContains = null,
    string? ExceptionTypeContains = null);

/// <summary>审计日志导出元数据响应。</summary>
public sealed record AuditLogExportMetadataResponse(
    int RowCount,
    bool Truncated,
    bool IncludesSensitiveFields,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FileName);
