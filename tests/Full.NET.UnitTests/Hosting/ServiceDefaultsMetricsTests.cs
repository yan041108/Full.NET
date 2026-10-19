using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Full.NET.UnitTests.Hosting;

/// <summary>单元测试（Hosting）：Service Defaults Metrics。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ServiceDefaultsMetricsTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("fullnet-resource-test")]
    public void Metrics_and_traces_share_logging_instance_and_configured_service_name(string? serviceName)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Environment.ApplicationName = "Full.NET.Host.Api";
        builder.Configuration["OTEL_SERVICE_NAME"] = serviceName;
        builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"] = "service.namespace=fullnet-test,service.instance.id=must-be-overridden";
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();
        var logging = host.Services.GetRequiredService<LoggingResourceMetadata>();
        var metrics = host.Services.GetRequiredService<MeterProvider>().GetResource();
        var tracing = host.Services.GetRequiredService<TracerProvider>().GetResource();
        foreach (var resource in new[] { metrics, tracing })
        {
            Assert.AreEqual(serviceName ?? builder.Environment.ApplicationName,
                resource.Attributes.Single(pair => pair.Key == "service.name").Value as string);
            Assert.AreEqual("fullnet-test",
                resource.Attributes.Single(pair => pair.Key == "service.namespace").Value as string);
            Assert.AreEqual(logging.Instance,
                resource.Attributes.Single(pair => pair.Key == "service.instance.id").Value as string);
        }
    }

    [TestMethod]
    public void Separate_hosts_have_distinct_telemetry_instances()
    {
        static string Instance()
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
            builder.AddFullNetServiceDefaults();
            using var host = builder.Build();
            var resource = host.Services.GetRequiredService<MeterProvider>().GetResource();
            return (string)resource.Attributes.Single(pair => pair.Key == "service.instance.id").Value;
        }
        Assert.AreNotEqual(Instance(), Instance());
    }

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

    [TestMethod]
    public void Host_disposal_unpublishes_its_logging_meter_instruments()
    {
        var instruments = new ConcurrentDictionary<Instrument, byte>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name != FullNetAsyncLogMonitor.MeterName) return;
            instruments.TryAdd(instrument, 0);
            meterListener.EnableMeasurementEvents(instrument);
        };
        var measurements = 0;
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
        {
            if (instruments.ContainsKey(instrument)) Interlocked.Increment(ref measurements);
        });
        listener.Start();
        // 排除启动监听前已存在的 Meter，只检查本 Host 新创建的监控器。
        instruments.Clear();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();
        _ = host.Services.GetRequiredService<FullNetLoggingMonitors>();
        listener.RecordObservableInstruments();
        Assert.IsTrue(Volatile.Read(ref measurements) > 0);
        host.Dispose();
        Interlocked.Exchange(ref measurements, 0);
        listener.RecordObservableInstruments();
        Assert.AreEqual(0, Volatile.Read(ref measurements));
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
