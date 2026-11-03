using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Full.NET.Messaging.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>测试专用 OpenMetrics 出口，只转发真实 Meter 采集值，不注入或改写健康样本。</summary>
internal sealed class MessagingMetricsTestExporter : IAsyncDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Dictionary<string, string> _samples = [];
    private readonly object _gate = new();
    private readonly WebApplication _app;
    internal int Port { get; }
    internal bool Unavailable { get; set; }

    private MessagingMetricsTestExporter(WebApplication app, int port)
    {
        _app = app; Port = port;
        _listener.InstrumentPublished = (instrument, listener) =>
        { if (instrument.Meter.Name == KafkaMessagingTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    internal static async Task<MessagingMetricsTestExporter> StartAsync()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls($"http://0.0.0.0:{port}");
        var app = builder.Build(); var exporter = new MessagingMetricsTestExporter(app, port);
        app.MapGet("/metrics", () => exporter.Unavailable ? Results.StatusCode(503) : Results.Text(exporter.Scrape(), "text/plain; version=0.0.4"));
        await app.StartAsync(); return exporter;
    }

    private string Scrape()
    {
        lock (_gate)
        {
            // 每次 scrape 重建快照，禁止把本次未观测的旧数值继续输出。
            _samples.Clear(); _listener.RecordObservableInstruments();
            return string.Join('\n', _samples.Values) + "\n";
        }
    }

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var name = instrument.Name.Replace('.', '_');
        if (instrument.Unit == "s" && !name.EndsWith("_seconds", StringComparison.Ordinal)) name += "_seconds";
        if (instrument.Unit == "h" && !name.EndsWith("_hours", StringComparison.Ordinal)) name += "_hours";
        var labels = tags.ToArray().Select(tag =>
        {
            Assert.IsTrue(KafkaMessagingTelemetry.AllowedTagKeys.Contains(tag.Key));
            var text = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? "";
            return tag.Key + "=\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        });
        var key = name + "{" + string.Join(',', labels) + "}";
        lock (_gate) _samples[key] = key + " " + value.ToString("R", CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync() { await _app.DisposeAsync(); _listener.Dispose(); }
}
