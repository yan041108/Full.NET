namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Host 操作日志查询权限。
/// </summary>
public static class OperationLogPermissions
{
    /// <summary>分页查询操作日志列表与详情。</summary>
    public const string Read = "auditing.operations.read";
}

/// <summary>已认证写操作审计汇总行响应。</summary>
public sealed record OperationLogResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ActionKey,
    string HttpMethod,
    string RequestPath,
    int StatusCode,
    int DurationMs,
    bool Succeeded,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint,
    string? PermissionCode);
