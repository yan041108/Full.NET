namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Host 访问日志查询权限。
/// </summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class AccessLogPermissions
{
    /// <summary>分页查询访问日志列表与详情。</summary>
    public const string Read = "auditing.access.read";
}

/// <summary>HTTP 访问审计汇总行响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。不含请求体、响应体与完整 IP，仅保存脱敏指纹。</remarks>
/// <param name="Id">访问日志稳定标识。</param>
/// <param name="OccurredAtUtc">请求发生时间（UTC）。</param>
/// <param name="HttpMethod">请求 HTTP 方法。</param>
/// <param name="RequestPath">请求路径。</param>
/// <param name="StatusCode">响应 HTTP 状态码。</param>
/// <param name="DurationMs">请求处理耗时（毫秒）。</param>
/// <param name="UserId">发起请求的用户标识；匿名请求为 <see langword="null"/>。</param>
/// <param name="TenantId">请求上下文租户标识；无租户上下文时为 <see langword="null"/>。</param>
/// <param name="TraceId">关联的分布式追踪标识。</param>
/// <param name="ClientIpFingerprint">客户端 IP 脱敏指纹；非明文 IP，用于同源聚合。</param>
/// <param name="IsAuthenticated">请求是否已通过身份认证。</param>
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

/// <summary>
/// Host 访问日志游标批次响应；不提供精确总数，避免深页查询固定执行 COUNT。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Items">当前页访问日志列表；顺序按发生时间倒序。</param>
/// <param name="NextCursor">下一页游标；为 <see langword="null"/> 时需结合 <paramref name="HasMore"/> 判断是否结束。</param>
/// <param name="HasMore">是否存在下一页；为 <see langword="false"/> 时 <paramref name="NextCursor"/> 为 <see langword="null"/>。</param>
public sealed record AccessLogCursorPageResponse(
    IReadOnlyList<AccessLogResponse> Items,
    string? NextCursor,
    bool HasMore);
