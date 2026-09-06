using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;

/// <summary>
/// 汇总 Serilog Elasticsearch Sink 配置与集群 <c>/_cluster/health</c> 探测结果，不回显凭据。
/// </summary>
internal sealed class ElasticsearchLogPipelineHealthService(
    IOptions<ElasticsearchLoggingOptions> options,
    IElasticsearchLogPipelineStatus pipelineStatus,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory)
{
    /// <summary>构建 Elasticsearch 日志管道健康快照。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康响应。</returns>
    public async Task<ElasticsearchLogPipelineHealthResponse> GetHealthAsync(
        CancellationToken cancellationToken)
    {
        var value = options.Value;
        var nodeEndpoints = value.NodeUris
            .Select(ElasticsearchEndpointRedactor.Redact)
            .Where(endpoint => endpoint is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!value.Enabled)
        {
            return BuildResponse(
                value,
                pipelineStatus,
                configuration,
                nodeEndpoints,
                clusterStatus: "disabled",
                clusterName: null,
                numberOfNodes: null,
                probeError: null);
        }

        if (nodeEndpoints.Length == 0)
        {
            return BuildResponse(
                value,
                pipelineStatus,
                configuration,
                nodeEndpoints,
                clusterStatus: "misconfigured",
                clusterName: null,
                numberOfNodes: null,
                probeError: "No valid Elasticsearch node endpoints are configured.");
        }

        var probeResult = await ProbeClusterHealthAsync(
                value,
                nodeEndpoints[0],
                cancellationToken)
            .ConfigureAwait(false);
        return BuildResponse(
            value,
            pipelineStatus,
            configuration,
            nodeEndpoints,
            probeResult.ClusterStatus,
            probeResult.ClusterName,
            probeResult.NumberOfNodes,
            probeResult.ProbeErrorMessage);
    }

    private async Task<ClusterProbeResult> ProbeClusterHealthAsync(
        ElasticsearchLoggingOptions value,
        string nodeEndpoint,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(ElasticsearchLogPipelineHealthService));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(nodeEndpoint, UriKind.Absolute), "_cluster/health"));
        if (!string.IsNullOrWhiteSpace(value.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "ApiKey",
                value.ApiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ClusterProbeResult(
                    "unreachable",
                    null,
                    null,
                    $"Elasticsearch cluster health probe returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "unknown"
                : "unknown";
            var clusterName = root.TryGetProperty("cluster_name", out var clusterElement)
                ? clusterElement.GetString()
                : null;
            int? numberOfNodes = root.TryGetProperty("number_of_nodes", out var nodesElement)
                && nodesElement.TryGetInt32(out var nodes)
                ? nodes
                : null;
            return new ClusterProbeResult(status, clusterName, numberOfNodes, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ClusterProbeResult(
                "unreachable",
                null,
                null,
                "Elasticsearch cluster health probe timed out.");
        }
        catch (HttpRequestException)
        {
            return new ClusterProbeResult(
                "unreachable",
                null,
                null,
                "Elasticsearch cluster health probe failed to connect.");
        }
        catch (JsonException)
        {
            return new ClusterProbeResult(
                "unreachable",
                null,
                null,
                "Elasticsearch cluster health probe returned invalid JSON.");
        }
    }

    private static ElasticsearchLogPipelineHealthResponse BuildResponse(
        ElasticsearchLoggingOptions value,
        IElasticsearchLogPipelineStatus pipelineStatus,
        IConfiguration configuration,
        IReadOnlyList<string> nodeEndpoints,
        string clusterStatus,
        string? clusterName,
        int? numberOfNodes,
        string? probeError) =>
        new(
            "serilog-elasticsearch",
            value.Enabled,
            pipelineStatus.IsSinkRegistered,
            value.IndexFormat,
            nodeEndpoints,
            !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]),
            value.PipelineNotice,
            clusterStatus,
            clusterName,
            numberOfNodes,
            probeError);

    private sealed record ClusterProbeResult(
        string ClusterStatus,
        string? ClusterName,
        int? NumberOfNodes,
        string? ProbeErrorMessage);
}
