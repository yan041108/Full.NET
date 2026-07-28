using Full.NET.Modules.Auditing.Features.WriteAuditBatch;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>
/// 访问日志写入载荷；不含 QueryString 与 Body，避免敏感数据落库。
/// </summary>
internal sealed record AccessLogWriteModel(
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
/// 将访问遥测写入请求作用域的固定槽位，由外层协调 Middleware 统一同步提交。
/// </summary>
internal sealed class AccessLogWriter(AuditWriteBuffer buffer)
{
    public void Capture(AccessLogWriteModel model) => buffer.Capture(model);
}
