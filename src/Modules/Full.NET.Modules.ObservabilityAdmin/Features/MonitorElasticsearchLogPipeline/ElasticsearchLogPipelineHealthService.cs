using Full.NET.Hosting.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;

/// <summary>分别报告固定投递入口和 ES 查询配置，不以配置或集群可达冒充投递确认。</summary>
internal sealed class ElasticsearchLogPipelineHealthService(
    IOptions<RuntimeLogTimelineOptions> options,
    ILoggingDeliverySelection deliverySelection,
    IConfiguration configuration)
{
    public Task<ElasticsearchLogPipelineHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = options.Value;
        var enabled = query.ElasticsearchEnabled && ElasticsearchTimelineReader.ValidConfiguration(query);
        return Task.FromResult(new ElasticsearchLogPipelineHealthResponse(
            deliverySelection.Mode switch
            {
                LoggingDeliveryMode.Local => "local",
                LoggingDeliveryMode.Collector => "external-collector",
                LoggingDeliveryMode.ApplicationKafka => "application-kafka",
                _ => throw new InvalidOperationException("Unsupported logging delivery mode."),
            },
            "configuration-only", enabled,
            enabled ? ElasticsearchEndpointRedactor.Redact(query.ElasticsearchEndpoint) : null,
            enabled ? query.ElasticsearchIndexPattern : null,
            !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])));
    }
}
