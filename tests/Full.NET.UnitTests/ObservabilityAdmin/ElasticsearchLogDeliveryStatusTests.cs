using System.Text.Json;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ObservabilityAdmin;

/// <summary>入口与查询配置独立，管理面不访问网络或声称远端投递成功。</summary>
[TestClass]
public sealed class ElasticsearchLogDeliveryStatusTests
{
    [TestMethod]
    public async Task All_modes_report_configuration_only_without_an_es_write_adapter()
    {
        foreach (var (mode, expected) in new[] {
            (LoggingDeliveryMode.Local, "local"),
            (LoggingDeliveryMode.Collector, "external-collector"),
            (LoggingDeliveryMode.ApplicationKafka, "application-kafka") })
        {
            var service = Create(new(), mode);
            var response = await service.GetHealthAsync(CancellationToken.None);
            Assert.AreEqual(expected, response.DeliveryStatus);
            Assert.AreEqual("configuration-only", response.DeliveryConfirmationBoundary);
            Assert.IsFalse(response.ElasticsearchQueryEnabled);
            Assert.IsNull(response.ElasticsearchEndpoint);
        }
    }

    [TestMethod]
    public async Task Es_query_configuration_never_exposes_credentials_or_implies_delivery()
    {
        var options = new RuntimeLogTimelineOptions { ElasticsearchEnabled = true,
            ElasticsearchEndpoint = "https://search.example.test:9243", ElasticsearchApiKey = "private-key" };
        var response = await Create(options, LoggingDeliveryMode.ApplicationKafka).GetHealthAsync(CancellationToken.None);
        Assert.IsTrue(response.ElasticsearchQueryEnabled);
        Assert.AreEqual("configuration-only", response.DeliveryConfirmationBoundary);
        Assert.DoesNotContain("private-key", JsonSerializer.Serialize(response));
        options.ElasticsearchEndpoint = "https://user:private-password@search.example.test";
        response = await Create(options, LoggingDeliveryMode.Local).GetHealthAsync(CancellationToken.None);
        Assert.IsFalse(response.ElasticsearchQueryEnabled);
        Assert.IsNull(response.ElasticsearchEndpoint);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            Create(options, LoggingDeliveryMode.Local).GetHealthAsync(new CancellationToken(true)));
    }

    private static ElasticsearchLogPipelineHealthService Create(RuntimeLogTimelineOptions options, LoggingDeliveryMode mode) =>
        new(Options.Create(options), new LoggingDeliverySelection(mode), new ConfigurationBuilder().Build());
}
