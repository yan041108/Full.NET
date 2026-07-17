using System.Collections.Concurrent;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class ServiceDefaultsMetricsTests
{
    [TestMethod]
    public void Localization_fallback_meter_is_collected_by_service_defaults()
    {
        var metricNames = new ConcurrentBag<string>();
        using var exporter = new MetricNameExporter(metricNames);
        using var reader = new BaseExportingMetricReader(exporter);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();
        builder.Services.ConfigureOpenTelemetryMeterProvider(
            metrics => metrics.AddReader(reader));
        using var host = builder.Build();
        var meterProvider = host.Services.GetRequiredService<MeterProvider>();
        var localizer = new ResourceErrorMessageLocalizer(
            [],
            new NamedMessageFormatter());

        localizer.Localize(
            new Error(
                Code: "common.missing",
                Message: "Safe fallback.",
                Type: ErrorType.Unexpected),
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.IsTrue(meterProvider.ForceFlush(5_000));
        CollectionAssert.Contains(
            metricNames.ToArray(),
            "fullnet.localization.error.fallbacks");
    }

    private sealed class MetricNameExporter(
        ConcurrentBag<string> metricNames) : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                metricNames.Add(metric.Name);
            }

            return ExportResult.Success;
        }
    }
}
