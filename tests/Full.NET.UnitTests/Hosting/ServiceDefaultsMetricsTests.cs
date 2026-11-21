using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Full.NET.UnitTests.Hosting;

/// <summary>单元测试（Hosting）：Service Defaults Metrics。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ServiceDefaultsMetricsTests
{
    [TestMethod]
    [DataRow("metrics")]
    [DataRow("traces")]
    [DataRow("both")]
    [DataRow("disabled")]
    [DataRow("global")]
    [DataRow("global-overrides")]
    public async Task Explicit_signal_endpoints_export_only_selected_signals_over_real_http(string mode)
    {
        var requests = new ConcurrentQueue<(string Path, string? ContentType, byte[] Body)>();
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.Logging.ClearProviders();
        receiverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var receiver = receiverBuilder.Build();
        receiver.MapPost("/{**path}", async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            requests.Enqueue((context.Request.Path.Value!, context.Request.ContentType, body.ToArray()));
            context.Response.ContentType = "application/x-protobuf";
            context.Response.StatusCode = StatusCodes.Status200OK;
        });
        await receiver.StartAsync();
        var address = receiver.Urls.Single();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = mode.StartsWith("global", StringComparison.Ordinal)
            ? address : null;
        builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        builder.Configuration["OTEL_EXPORTER_OTLP_TIMEOUT"] = "2000";
        var hasMetrics = mode is "metrics" or "both" or "global-overrides";
        var hasTraces = mode is "traces" or "both" or "global-overrides";
        builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = hasMetrics ? address + "/custom/metrics" : null;
        builder.Configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = hasTraces ? address + "/custom/traces" : null;
        builder.AddFullNetServiceDefaults();
        builder.Services.ConfigureOpenTelemetryTracerProvider(traces => traces.AddSource("fullnet-otlp-test"));
        using var host = builder.Build();
        var meterProvider = host.Services.GetRequiredService<MeterProvider>();
        var tracerProvider = host.Services.GetRequiredService<TracerProvider>();
        using var meter = new Meter("Full.NET.Logging.Kafka");
        meter.CreateCounter<long>("fullnet.otlp.registration.probe").Add(7);
        using var source = new ActivitySource("fullnet-otlp-test");
        using (var activity = source.StartActivity("fullnet-otlp-registration-probe"))
            Assert.IsNotNull(activity);
        host.Services.GetRequiredService<ILogger<ServiceDefaultsMetricsTests>>().LogInformation("OTLP registration fixture");
        Assert.IsTrue(meterProvider.ForceFlush(5_000));
        Assert.IsTrue(tracerProvider.ForceFlush(5_000));

        var captured = requests.ToArray();
        var expectedMetrics = hasMetrics || mode == "global";
        var expectedTraces = hasTraces || mode == "global";
        var metricRequests = captured.Where(item => item.Path.EndsWith("/metrics", StringComparison.Ordinal)).ToArray();
        var traceRequests = captured.Where(item => item.Path.EndsWith("/traces", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(expectedMetrics ? 1 : 0, metricRequests);
        Assert.HasCount(expectedTraces ? 1 : 0, traceRequests);
        if (!mode.StartsWith("global", StringComparison.Ordinal))
            Assert.HasCount(metricRequests.Length + traceRequests.Length, captured);
        foreach (var item in metricRequests.Concat(traceRequests))
        {
            Assert.AreEqual("application/x-protobuf", item.ContentType);
            Assert.IsGreaterThan(0, item.Body.Length);
        }
        if (expectedMetrics)
        {
            Assert.AreEqual(hasMetrics ? "/custom/metrics" : "/v1/metrics", metricRequests[0].Path);
            StringAssert.Contains(Encoding.UTF8.GetString(metricRequests[0].Body), "fullnet.otlp.registration.probe");
            var logging = host.Services.GetRequiredService<LoggingResourceMetadata>();
            StringAssert.Contains(Encoding.UTF8.GetString(metricRequests[0].Body), logging.Instance);
        }
        if (expectedTraces)
        {
            Assert.AreEqual(hasTraces ? "/custom/traces" : "/v1/traces", traceRequests[0].Path);
            StringAssert.Contains(Encoding.UTF8.GetString(traceRequests[0].Body), "fullnet-otlp-registration-probe");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Signal_options_are_isolated_and_override_common_protocol_headers_timeout_and_compression(bool overrides)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://127.0.0.1:1/v1/metrics";
        builder.Configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = "http://127.0.0.1:1/custom";
        builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"] = "x-common=fixture";
        builder.Configuration["OTEL_EXPORTER_OTLP_TIMEOUT"] = "3210";
        builder.Configuration["OTEL_EXPORTER_OTLP_COMPRESSION"] = "gzip";
        if (overrides)
        {
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_PROTOCOL"] = "grpc";
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_HEADERS"] = "x-metrics=fixture";
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_TIMEOUT"] = "1234";
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_COMPRESSION"] = "none";
        }
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();
        _ = host.Services.GetRequiredService<MeterProvider>();
        _ = host.Services.GetRequiredService<TracerProvider>();
        var options = host.Services.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>();
        var metrics = options.Get("fullnet-metrics");
        var traces = options.Get("fullnet-traces");
        Assert.AreEqual("http://127.0.0.1:1/v1/metrics", metrics.Endpoint.AbsoluteUri);
        Assert.AreEqual("http://127.0.0.1:1/custom", traces.Endpoint.AbsoluteUri);
        Assert.AreEqual(overrides ? "Grpc" : "HttpProtobuf", metrics.Protocol.ToString());
        Assert.AreEqual(OtlpExportProtocol.HttpProtobuf, traces.Protocol);
        Assert.AreEqual(overrides ? "x-metrics=fixture" : "x-common=fixture", metrics.Headers);
        Assert.AreEqual("x-common=fixture", traces.Headers);
        Assert.AreEqual(overrides ? 1234 : 3210, metrics.TimeoutMilliseconds);
        Assert.AreEqual(3210, traces.TimeoutMilliseconds);
        Assert.AreEqual(overrides ? OtlpExportCompression.None : OtlpExportCompression.GZip, metrics.Compression);
        Assert.AreEqual(OtlpExportCompression.GZip, traces.Compression);
    }

    [TestMethod]
    [DataRow("ENDPOINT", "relative")]
    [DataRow("ENDPOINT", "ftp://host/path")]
    [DataRow("ENDPOINT", "https://user:secret-value@host/path")]
    [DataRow("ENDPOINT", "https://host/path#secret-value")]
    [DataRow("PROTOCOL", "http/json")]
    [DataRow("TIMEOUT", "0")]
    [DataRow("TIMEOUT", "-1")]
    [DataRow("TIMEOUT", "2147483648")]
    [DataRow("COMPRESSION", "secret-value")]
    public void Invalid_signal_configuration_fails_before_host_build_without_echoing_values(string suffix, string value)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "https://host/v1/metrics";
        builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_" + suffix] = value;
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => builder.AddFullNetServiceDefaults());
        StringAssert.Contains(error.Message, "OTEL_EXPORTER_OTLP_METRICS_" + suffix);
        Assert.IsFalse(error.Message.Contains("secret-value", StringComparison.Ordinal));
    }

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
