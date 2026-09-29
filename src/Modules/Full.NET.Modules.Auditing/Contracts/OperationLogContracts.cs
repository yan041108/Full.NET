namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Host 操作日志查询权限。
/// </summary>
public static class OperationLogPermissions
{
    /// <summary>分页查询操作日志列表与详情。</summary>
    public const string Read = "auditing.operations.read";

    /// <summary>读取未到期的受限操作详情；还需具备普通操作日志读取权限。</summary>
    public const string ReadDetails = "auditing.operations.details.read";
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

/// <summary>版本 1 的受限网络上下文；仅由独立详情接口返回。</summary>
public sealed record OperationLogDetailsContextV1(
    int SchemaVersion,
    string? ClientIp,
    int? ClientPort,
    string? ServerIp,
    int? ServerPort,
    string? RequestCaptureState = null,
    OperationLogExportRequestSummaryV1? RequestSummary = null,
    string? ResponseCaptureState = null,
    OperationLogExportResponseSummaryV1? ResponseSummary = null);

/// <summary>操作日志导出请求的固定字段摘要；筛选文本不进入详情。</summary>
public sealed record OperationLogExportRequestSummaryV1(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int? StatusCode,
    bool? Succeeded);

/// <summary>操作日志导出结果的固定字段摘要；不包含文件名或文件内容。</summary>
public sealed record OperationLogExportResponseSummaryV1(
    int RowCount,
    bool Truncated,
    bool IncludesSensitiveFields);

/// <summary>未到期的受限操作详情响应，不进入列表、普通详情或导出。</summary>
public sealed record OperationLogDetailsResponse(
    Guid Id,
    DateTimeOffset DetailsExpiresAtUtc,
    OperationLogDetailsContextV1 Context);
