namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>Elasticsearch 日志管道健康响应。</summary>
/// <param name="AdapterKind">适配器类型，固定为 serilog-elasticsearch。</param>
/// <param name="IsEnabled">配置是否声明启用 Sink。</param>
/// <param name="IsSinkRegistered">宿主启动时 Sink 是否成功注册。</param>
/// <param name="IndexFormat">索引格式。</param>
/// <param name="NodeEndpoints">脱敏后的节点端点列表。</param>
/// <param name="OpenTelemetryOtlpEndpointConfigured">是否配置了 OTEL_EXPORTER_OTLP_ENDPOINT。</param>
/// <param name="PipelineNotice">管道边界说明，强调日志不经 OTel 重复采集。</param>
/// <param name="ClusterStatus">集群健康状态；未启用时为 disabled，探测失败为 unreachable。</param>
/// <param name="ClusterName">集群名称。</param>
/// <param name="NumberOfNodes">节点数量。</param>
/// <param name="ProbeErrorMessage">探测失败时的稳定摘要；不含凭据。</param>
public sealed record ElasticsearchLogPipelineHealthResponse(
    string AdapterKind,
    bool IsEnabled,
    bool IsSinkRegistered,
    string IndexFormat,
    IReadOnlyList<string> NodeEndpoints,
    bool OpenTelemetryOtlpEndpointConfigured,
    string PipelineNotice,
    string ClusterStatus,
    string? ClusterName,
    int? NumberOfNodes,
    string? ProbeErrorMessage);
