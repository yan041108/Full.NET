namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>访问请求的最小元数据；不保存查询参数、请求体、Cookie 或令牌。</summary>
internal sealed record AccessLogWriteModel(
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
