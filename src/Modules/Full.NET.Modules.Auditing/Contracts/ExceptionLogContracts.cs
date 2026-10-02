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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">异常日志唯一标识。</param>
/// <param name="OccurredAtUtc">异常发生时间（UTC）。</param>
/// <param name="ExceptionType">异常类型全名（含命名空间）。</param>
/// <param name="Message">异常消息；可能包含敏感信息，前端展示须遵守脱敏策略。</param>
/// <param name="StackTrace">异常堆栈；null 表示未捕获或已被裁剪。</param>
/// <param name="HttpMethod">触发异常的请求 HTTP 方法；非 HTTP 上下文为 null。</param>
/// <param name="RequestPath">触发异常的请求路径；非 HTTP 上下文为 null。</param>
/// <param name="UserId">触发异常的用户 Id；匿名请求为 null。</param>
/// <param name="TenantId">触发异常的租户 Id；Host 级请求为 null。</param>
/// <param name="TraceId">分布式追踪 TraceId；便于在链路系统中定位上下文。</param>
/// <param name="ClientIpFingerprint">客户端 IP 指纹（非明文 IP），用于定位来源。</param>
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
