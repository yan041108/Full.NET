namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>Host 出站调用审计查询权限。</summary>
public static class OutboundCallLogPermissions
{
    /// <summary>分页查询出站调用审计列表与详情。</summary>
    public const string Read = "auditing.outbound_calls.read";
}

/// <summary>出站调用审计汇总行响应；不包含请求/响应正文或凭据。</summary>
public sealed record OutboundCallLogResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ProviderKey,
    string OperationKey,
    string DestinationHostCategory,
    int StatusCode,
    bool Succeeded,
    int DurationMs,
    int RetryCount,
    string? TraceId,
    string? SafeErrorCode,
    Guid? TenantId,
    Guid? UserId);

/// <summary>供调用方显式写入的安全出站审计元数据。</summary>
public sealed record OutboundCallAuditRequest(
    string ProviderKey,
    string OperationKey,
    string DestinationHostCategory,
    int StatusCode,
    bool Succeeded,
    int DurationMs,
    int RetryCount,
    string? TraceId = null,
    string? SafeErrorCode = null,
    Guid? TenantId = null,
    Guid? UserId = null);

/// <summary>Testing 探针请求；允许携带恶意样本以验证脱敏，不会原样持久化。</summary>
public sealed record OutboundCallAuditProbeRequest(
    OutboundCallAuditRequest Audit,
    string? SensitiveProbeMarker = null);
