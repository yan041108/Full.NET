namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>Host 出站调用审计查询权限。</summary>
public static class OutboundCallLogPermissions
{
    /// <summary>分页查询出站调用审计列表与详情。</summary>
    public const string Read = "auditing.outbound_calls.read";
}

/// <summary>出站调用审计汇总行响应；不包含请求/响应正文或凭据。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">审计记录唯一标识。</param>
/// <param name="OccurredAtUtc">出站调用发生时间（UTC）。</param>
/// <param name="ProviderKey">出站服务提供者键；用于路由和聚合。</param>
/// <param name="OperationKey">出站操作键；同一 ProviderKey 下区分不同操作。</param>
/// <param name="DestinationHostCategory">目标主机类别；用于安全分类，不暴露具体主机名。</param>
/// <param name="StatusCode">HTTP 或协议状态码。</param>
/// <param name="Succeeded">是否成功；失败时 <see cref="SafeErrorCode"/> 给出原因。</param>
/// <param name="DurationMs">调用耗时（毫秒）。</param>
/// <param name="RetryCount">重试次数；含首次调用。</param>
/// <param name="TraceId">链路追踪标识；无上下文时为 <see langword="null"/>。</param>
/// <param name="SafeErrorCode">已脱敏的安全错误码；成功或无码时为 <see langword="null"/>。</param>
/// <param name="TenantId">租户标识；系统级调用为 <see langword="null"/>。</param>
/// <param name="UserId">用户标识；非用户上下文为 <see langword="null"/>。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。调用方必须自行脱敏，不得写入凭据、密钥或原始请求/响应正文。</remarks>
/// <param name="ProviderKey">出站服务提供者键；用于路由和聚合。</param>
/// <param name="OperationKey">出站操作键；同一 ProviderKey 下区分不同操作。</param>
/// <param name="DestinationHostCategory">目标主机类别；用于安全分类，不暴露具体主机名。</param>
/// <param name="StatusCode">HTTP 或协议状态码。</param>
/// <param name="Succeeded">是否成功；失败时填写 <paramref name="SafeErrorCode"/>。</param>
/// <param name="DurationMs">调用耗时（毫秒）。</param>
/// <param name="RetryCount">重试次数；含首次调用。</param>
/// <param name="TraceId">链路追踪标识；默认 <see langword="null"/>。</param>
/// <param name="SafeErrorCode">已脱敏的安全错误码；默认 <see langword="null"/>。</param>
/// <param name="TenantId">租户标识；系统级调用留 <see langword="null"/>。</param>
/// <param name="UserId">用户标识；非用户上下文留 <see langword="null"/>。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Audit">出站审计元数据；可包含待脱敏的探针样本。</param>
/// <param name="SensitiveProbeMarker">敏感探针标记；仅用于脱敏验证，不会写入持久存储。</param>
public sealed record OutboundCallAuditProbeRequest(
    OutboundCallAuditRequest Audit,
    string? SensitiveProbeMarker = null);
