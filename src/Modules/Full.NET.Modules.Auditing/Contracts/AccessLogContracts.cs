namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Host 访问日志查询权限。
/// </summary>
public static class AccessLogPermissions
{
    /// <summary>分页查询访问日志列表与详情。</summary>
    public const string Read = "auditing.access.read";
}

/// <summary>HTTP 访问审计汇总行响应。</summary>
public sealed record AccessLogResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string HttpMethod,
    string RequestPath,
    int StatusCode,
    int DurationMs,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint,
    bool IsAuthenticated);
