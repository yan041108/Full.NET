using System.Net;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ObservabilityAdmin;

[TestClass]
public sealed class ElasticsearchLogDeliveryStatusTests
{
    [TestMethod]
    public async Task Disabled_es_reports_application_boundary_without_network_probe()
    {
        foreach (var (mode, expectedStatus) in new[]
        {
            ((LoggingDeliveryMode?)null, "legacy-console"),
            (LoggingDeliveryMode.Local, "disabled"),
            (LoggingDeliveryMode.Collector, "external-collector"),
        })
        {
            var service = CreateService(
                new ElasticsearchLoggingOptions(),
                new LoggingDeliverySelection(mode, false),
                new RejectingHttpClientFactory());

            var response = await service.GetHealthAsync(CancellationToken.None);

            Assert.AreEqual(expectedStatus, response.DeliveryStatus);
            Assert.AreEqual("configuration-only", response.DeliveryConfirmationBoundary);
            Assert.AreEqual("disabled", response.ClusterStatus);
        }
    }

    [TestMethod]
    public async Task Legacy_direct_reports_sink_registration_without_claiming_delivery()
    {
        var service = CreateService(
            new ElasticsearchLoggingOptions
            {
                Enabled = true,
                NodeUris = ["https://search.example.test:9243"],
                ApiKey = "secret-key",
            },
            new LoggingDeliverySelection(null, true),
            new StaticHttpClientFactory());

        var response = await service.GetHealthAsync(CancellationToken.None);

        Assert.AreEqual("legacy-direct", response.DeliveryStatus);
        Assert.AreEqual("sink-registered-only", response.DeliveryConfirmationBoundary);
        Assert.AreEqual("green", response.ClusterStatus);
        Assert.DoesNotContain("secret-key", string.Join(' ', response.NodeEndpoints));
    }

    private static ElasticsearchLogPipelineHealthService CreateService(
        ElasticsearchLoggingOptions options,
        ILoggingDeliverySelection selection,
        IHttpClientFactory httpClientFactory) =>
        new(
            Options.Create(options),
            new ElasticsearchLogPipelineRegistration
            {
                IsEnabled = options.Enabled,
                IsSinkRegistered = options.Enabled,
            },
            selection,
            new ConfigurationBuilder().Build(),
            httpClientFactory);

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new AssertFailedException("disabled Elasticsearch must not create a client");
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StaticResponseHandler());
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"green\",\"cluster_name\":\"logs\",\"number_of_nodes\":2}"),
            });
    }
}
