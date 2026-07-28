namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Auditing 模块对外返回的稳定错误码。
/// </summary>
public static class AuditingErrorCodes
{
    /// <summary>Auditing 错误码前缀。</summary>
    public const string Prefix = "auditing.";

    /// <summary>目标访问日志不存在。</summary>
    public const string AccessLogNotFound = "auditing.access_log.not_found";

    /// <summary>访问日志游标无效、版本未知或与当前筛选不匹配。</summary>
    public const string AccessLogCursorInvalid = "auditing.access_log.cursor_invalid";

    /// <summary>目标操作日志不存在。</summary>
    public const string OperationLogNotFound = "auditing.operation_log.not_found";

    /// <summary>目标异常日志不存在。</summary>
    public const string ExceptionLogNotFound = "auditing.exception_log.not_found";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        AccessLogNotFound,
        AccessLogCursorInvalid,
        OperationLogNotFound,
        ExceptionLogNotFound,
    ]);
}
