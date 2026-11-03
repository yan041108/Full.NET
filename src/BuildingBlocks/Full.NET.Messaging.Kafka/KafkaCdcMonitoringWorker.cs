using System.Diagnostics.Metrics;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>串行有界观测当前数据库与专用试点资源；无写入、恢复或控制面副作用。</summary>
internal sealed partial class KafkaCdcMonitoringWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly KafkaCdcMonitoringOptions _options;
    private readonly KafkaConnectRollbackOptions _control;
    private readonly KafkaMessagingOptions _kafka;
    private readonly IKafkaConnectAdminClient _connect;
    private readonly KafkaConsumerLagObserver _lag;
    private readonly MessagingRuntimeObservation _samples;
    private readonly Meter _meter = new(KafkaMessagingTelemetry.MeterName);
    private string? _platformComponent;
    private string? _platformCode;

    public KafkaCdcMonitoringWorker(IServiceScopeFactory scopes, IOptions<KafkaCdcMonitoringOptions> options,
        IOptions<KafkaConnectRollbackOptions> control, IOptions<KafkaMessagingOptions> kafka,
        IKafkaConnectAdminClient connect, KafkaConsumerLagObserver lag)
    {
        _scopes = scopes; _options = options.Value; _control = control.Value; _kafka = kafka.Value;
        _connect = connect; _lag = lag;
        _samples = new(TimeProvider.System, TimeSpan.FromSeconds(_options.MaximumSampleAgeSeconds));
        _meter.CreateObservableGauge("fullnet.messaging.observation.status", _samples.ObserveStatus);
        _meter.CreateObservableGauge("fullnet.messaging.observation.timestamp", () => _samples.ObserveTimestamps(), unit: "s");
        _meter.CreateObservableGauge("fullnet.messaging.observation.maximum_age", () => _samples.ObserveTimestamps(ageLimit: true), unit: "s");
        _meter.CreateObservableGauge("fullnet.messaging.cdc.sqlserver.capture_job_running", () => _samples.ObserveValue("capture_job"));
        _meter.CreateObservableGauge("fullnet.messaging.cdc.mysql.binlog_retention_hours", () => _samples.ObserveValue("binlog_retention"), unit: "h");
        _meter.CreateObservableGauge("fullnet.messaging.connector.running", () => _samples.ObserveValue("connector"));
        _meter.CreateObservableGauge("fullnet.messaging.producer.fenced", () => _samples.ObserveValue("producer_fence"));
        _meter.CreateObservableGauge("fullnet.messaging.kafka.observed_topic.lag", () => _samples.ObserveValue("consumer_lag"), unit: "{message}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _samples.Set("collector", "cdc", _options.Enabled ? "unknown" : "disabled");
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task SampleAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.SampleTimeoutSeconds));
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var snapshot = await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>().ReadAsync(deadline.Token).ConfigureAwait(false);
            if (snapshot.Provider == CdcDeliveryPosition.SqlServerProvider)
            {
                _platformComponent = "capture_job"; _platformCode = "sqlserver";
                _samples.Set("capture_job", "sqlserver", snapshot.CaptureJobRunning is null ? "unknown"
                    : snapshot.CaptureJobRunning.Value ? "healthy" : "unhealthy", snapshot.CaptureJobRunning is { } running ? running ? 1 : 0 : null);
            }
            else if (snapshot.Provider == CdcDeliveryPosition.MySqlProvider)
            {
                _platformComponent = "binlog_retention"; _platformCode = "mysql";
                _samples.Set("binlog_retention", "mysql", snapshot.BinlogRetentionHours is null ? "unknown"
                    : snapshot.BinlogRetentionHours is > 0 and < 24 ? "unhealthy" : "healthy", snapshot.BinlogRetentionHours);
            }
            else throw new InvalidOperationException("CDC 采集提供程序未知。");
            _samples.Set("producer_fence", "database", snapshot.ProducerFenced ? "unhealthy" : "healthy", snapshot.ProducerFenced ? 1 : 0);
            _samples.Set("database", "cdc", "healthy");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // 不记录异常文本和连接信息；失败立即撤下先前数值，不能继续使用上一次健康结果。
            _samples.Set("database", "cdc", "error");
            if (_platformComponent is not null) _samples.Set(_platformComponent, _platformCode!, "unknown");
            _samples.Set("producer_fence", "database", "unknown");
        }

        await SampleBindingsAsync(deadline.Token, token).ConfigureAwait(false);
    }

    internal static bool? ReadConnectorRunning(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("connector", out var connector) || !connector.TryGetProperty("state", out var state)
            || !root.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() == 0)
            return null;
        var running = state.GetString() == "RUNNING";
        foreach (var task in tasks.EnumerateArray())
        {
            if (!task.TryGetProperty("state", out var taskState)) return null;
            running &= taskState.GetString() == "RUNNING";
        }
        return running;
    }

    public override void Dispose() { base.Dispose(); InvalidateStreams("disabled"); _meter.Dispose(); }
}
