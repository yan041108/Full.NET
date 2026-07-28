using Full.NET.Modules.Auditing.Features.WriteAuditBatch;

namespace Full.NET.Modules.Auditing.Features.WriteOperationLogs;

/// <summary>
/// 操作日志写入载荷；仅汇总写操作元数据，不含 Body。
/// </summary>
internal sealed record OperationLogWriteModel(
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

/// <summary>
/// 将操作审计摘要写入请求作用域的固定槽位，由外层协调 Middleware 统一同步提交。
/// </summary>
internal sealed class OperationLogWriter(AuditWriteBuffer buffer)
{
    public void Capture(OperationLogWriteModel model) => buffer.Capture(model);
}
