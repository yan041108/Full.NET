namespace Full.NET.Hosting.Observability;

/// <summary>
/// 经现有 Serilog 管道写入 Elasticsearch 的可选配置；日志不重复经 OTel Logs Exporter 采集。
/// </summary>
public sealed class ElasticsearchLoggingOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Logging:Elasticsearch";

    /// <summary>是否启用 Elasticsearch 日志 Sink。</summary>
    public bool Enabled { get; init; }

    /// <summary>Elasticsearch 节点 URI 列表；不得包含内嵌凭据。</summary>
    public IReadOnlyList<string> NodeUris { get; init; } = Array.Empty<string>();

    /// <summary>索引格式，支持 Serilog 日期占位符。</summary>
    public string IndexFormat { get; init; } = "fullnet-logs-{0:yyyy.MM.dd}";

    /// <summary>可选 ApiKey，通过 Authorization 头传递；不得回显到健康 API。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>写入 Elasticsearch 的最低日志级别。</summary>
    public string MinimumLevel { get; init; } = "Information";

    /// <summary>部署说明：日志经 Serilog Sink 导出，Trace/Metrics 仍由 OTel OTLP 负责。</summary>
    public string PipelineNotice { get; init; } =
        "结构化日志经 Serilog Elasticsearch Sink 从现有 Full.NET 双通道管道导出；OpenTelemetry OTLP 仅承载 Trace/Metrics，避免重复采集日志。";
}

/// <summary>运行时 Elasticsearch 日志管道注册状态。</summary>
public interface IElasticsearchLogPipelineStatus
{
    /// <summary>配置已启用且 Sink 已成功注册。</summary>
    bool IsSinkRegistered { get; }

    /// <summary>配置声明为启用。</summary>
    bool IsEnabled { get; }
}

/// <summary>记录 Elasticsearch Sink 是否已在宿主启动时注册。</summary>
internal sealed class ElasticsearchLogPipelineRegistration : IElasticsearchLogPipelineStatus
{
    public bool IsSinkRegistered { get; internal set; }

    public bool IsEnabled { get; internal set; }
}
