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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">操作日志行唯一标识。</param>
/// <param name="OccurredAtUtc">操作发生时间（UTC）。</param>
/// <param name="ActionKey">稳定审计动作键；发布后不可改名。</param>
/// <param name="HttpMethod">请求 HTTP 方法。</param>
/// <param name="RequestPath">请求路径。</param>
/// <param name="StatusCode">HTTP 响应状态码。</param>
/// <param name="DurationMs">请求耗时（毫秒）。</param>
/// <param name="Succeeded">本次操作是否成功。</param>
/// <param name="UserId">发起操作的用户标识；匿名访问时为 <see langword="null"/>。</param>
/// <param name="TenantId">操作所属租户标识；宿主上下文时为 <see langword="null"/>。</param>
/// <param name="TraceId">分布式追踪标识；未启用时为 <see langword="null"/>。</param>
/// <param name="ClientIpFingerprint">客户端 IP 指纹（脱敏后）；未采集时为 <see langword="null"/>。</param>
/// <param name="PermissionCode">命中的权限码；未命中权限校验时为 <see langword="null"/>。</param>
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
