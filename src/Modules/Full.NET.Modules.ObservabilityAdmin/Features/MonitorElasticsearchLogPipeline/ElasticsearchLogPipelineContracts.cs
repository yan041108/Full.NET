namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;

/// <summary>日志入口及 ES 查询配置事实，不包含凭据或远端投递成功判断。</summary>
/// <param name="DeliveryStatus">启动入口：local、external-collector 或 application-kafka。</param>
/// <param name="DeliveryConfirmationBoundary">固定 configuration-only，远端确认须查实际指标。</param>
/// <param name="ElasticsearchQueryEnabled">启用有效的 ES 时间线查询配置，不是写入开关。</param>
/// <param name="ElasticsearchEndpoint">无凭据查询根地址，未启用时为空。</param>
/// <param name="ElasticsearchIndexPattern">查询索引范围，未启用时为空。</param>
/// <param name="OpenTelemetryOtlpEndpointConfigured">是否配置 Trace/Metrics 的 OTLP 地址。</param>
public sealed record ElasticsearchLogPipelineHealthResponse(
    string DeliveryStatus,
    string DeliveryConfirmationBoundary,
    bool ElasticsearchQueryEnabled,
    string? ElasticsearchEndpoint,
    string? ElasticsearchIndexPattern,
    bool OpenTelemetryOtlpEndpointConfigured);
