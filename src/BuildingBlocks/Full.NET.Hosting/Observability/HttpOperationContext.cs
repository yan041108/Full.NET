namespace Full.NET.Hosting.Observability;

/// <summary>
/// 单次 HTTP 完成日志的 Internal 摘要；原始地址、端口及请求/响应内容不得进入此模型。
/// </summary>
internal sealed record HttpOperationContext(
    string RouteTemplate,
    string? EndpointName,
    string? EndpointDisplayName,
    string? Controller,
    string? Action,
    string? Area,
    string HttpMethod,
    string? Scheme,
    string? HttpProtocol,
    string? SourceOriginFingerprint,
    string? UserAgentSummary,
    string? Culture,
    string? AcceptLanguageSummary,
    string? ClientIdFingerprint,
    string? ClientIpFingerprint,
    string? TraceId,
    string? SpanId,
    string RequestId,
    int? CaptureThreadId);
