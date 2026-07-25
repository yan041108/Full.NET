namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Host 异常日志查询权限。
/// </summary>
public static class ExceptionLogPermissions
{
    /// <summary>分页查询异常日志列表与详情。</summary>
    public const string Read = "auditing.exceptions.read";
}

/// <summary>未处理异常审计汇总行响应。</summary>
public sealed record ExceptionLogResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? HttpMethod,
    string? RequestPath,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint);
