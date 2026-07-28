using Full.NET.Modules.Auditing.Features.WriteAuditBatch;

namespace Full.NET.Modules.Auditing.Features.WriteExceptionLogs;

/// <summary>
/// 异常日志写入载荷；消息与堆栈已截断，不含请求体。
/// </summary>
internal sealed record ExceptionLogWriteModel(
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? HttpMethod,
    string? RequestPath,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint);

/// <summary>
/// 将异常审计摘要写入请求作用域的固定槽位，由外层协调 Middleware 统一同步提交。
/// </summary>
internal sealed class ExceptionLogWriter(AuditWriteBuffer buffer)
{
    public void Capture(ExceptionLogWriteModel model) => buffer.Capture(model);
}
