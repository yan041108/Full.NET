using Full.NET.Abstractions.Tenancy;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Messaging.Kafka;

internal sealed partial class KafkaCdcMonitoringWorker
{
    private int _nextStream;

    private async Task SampleBindingsAsync(CancellationToken deadline, CancellationToken stopping)
    {
        if (!_control.Enabled || !_kafka.Enabled)
        {
            InvalidateStreams("disabled");
            _samples.Set("control", "cdc", "disabled");
            _samples.Set("collector", "cdc", "healthy");
            return;
        }
        // 配置和标签数量有界；每条流仍由真实Connector源/路由校验，观测健康不等于授予业务发布所有权。
        if (_control.Streams.Length is < 1 or > 64
            || _control.Streams.Select(binding => binding.TopicName).Distinct(StringComparer.Ordinal).Count() != _control.Streams.Length
            || (_control.Streams.Length > 1 && !string.IsNullOrWhiteSpace(_options.RetentionTimeTopicId))
            || _options.RetentionTimeTopics.Keys.Any(topic => !_control.Streams.Any(binding => binding.TopicName == topic)))
        {
            InvalidateStreams("unknown");
            _samples.Set("control", "cdc", "error");
            return;
        }
        var lag = new Dictionary<string, KafkaConsumerLagSnapshot?>(StringComparer.Ordinal);
        // 全轮共用原期限，轮转起点避免慢流永远耗尽固定尾部流的预算。
        var first = _nextStream; _nextStream = (first + 1) % _control.Streams.Length;
        try
        {
            foreach (var binding in _control.Streams.Skip(first).Concat(_control.Streams.Take(first)))
            {
                lag[binding.TopicName] = await SampleStreamAsync(binding, ResourceCode(binding), deadline, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { InvalidateStreams("unknown"); throw; }
        foreach (var group in _control.Streams.GroupBy(binding => binding.ConsumerGroupId, StringComparer.Ordinal))
        {
            var values = group.Where(binding => lag.ContainsKey(binding.TopicName)).Select(binding => lag[binding.TopicName]).ToArray();
            if (values.Length == 0 || values.Any(value => value is null))
            {
                KafkaMessagingTelemetry.InvalidateConsumerLag(group.Key); continue;
            }
            try
            {
                var total = values.Sum(value => value!.TotalLagMessages);
                double? ratio = values.All(value => value!.LagRetentionRatio is not null) ? values.Max(value => value!.LagRetentionRatio) : null;
                KafkaMessagingTelemetry.UpdateConsumerLag("kafka", group.Key, total, ratio,
                    TimeSpan.FromSeconds(_options.MaximumSampleAgeSeconds));
            }
            catch (OverflowException) { KafkaMessagingTelemetry.InvalidateConsumerLag(group.Key); }
        }
        _samples.Set("control", "cdc", "healthy");
        _samples.Set("collector", "cdc", "healthy");
    }

    private string ResourceCode(KafkaConnectRollbackStreamBinding binding) => _control.Streams.Length == 1 ? "pilot" : binding.TopicName;

    private void InvalidateStream(KafkaConnectRollbackStreamBinding binding, string state)
    {
        var code = ResourceCode(binding);
        KafkaMessagingTelemetry.InvalidateConnectorHealth(code);
        KafkaMessagingTelemetry.InvalidateConsumerLag(binding.ConsumerGroupId);
        foreach (var component in new[] { "connector", "consumer_lag", "connector_offset", "connector_lag", "lag_retention" })
            _samples.Set(component, code, state);
    }

    private void InvalidateStreams(string state)
    {
        foreach (var binding in _control.Streams) InvalidateStream(binding, state);
    }

    private async Task<KafkaConsumerLagSnapshot?> SampleStreamAsync(KafkaConnectRollbackStreamBinding binding, string code,
        CancellationToken deadline, CancellationToken stopping)
    {
        KafkaConsumerLagSnapshot? observation = null;
        var retentionId = _options.RetentionTimeTopics.GetValueOrDefault(binding.TopicName, _options.RetentionTimeTopicId);

        // 新采样在期限内完成后原子替换；提前撤下仍新鲜的数值会在每轮 scrape 中重置告警 for 计时。
        var routeValidated = false;
        try
        {
            // 同名 Connector 可以被重建或改用途；先核对正式源/路由，不能把任意 RUNNING 任务当作本试点健康。
            var configuration = await _connect.TryReadConnectorConfigurationAsync(binding.ConnectorName, deadline).ConfigureAwait(false);
            if (configuration is null || !KafkaConnectResourceIdentity.IsTargetRoute(configuration, binding))
            {
                KafkaMessagingTelemetry.InvalidateConnectorHealth(code);
                KafkaMessagingTelemetry.InvalidateConsumerLag(binding.ConsumerGroupId);
                _samples.Set("connector", code, "error");
                _samples.Set("consumer_lag", code, "unknown");
                _samples.Set("connector_offset", code, "unknown");
                _samples.Set("connector_lag", code, "unknown");
                _samples.Set("lag_retention", code, "unknown");
                return null;
            }
            routeValidated = true;
            var json = await _connect.TryGetConnectorStatusAsync(binding.ConnectorName, deadline).ConfigureAwait(false);
            var healthy = ReadConnectorRunning(json);
            _samples.Set("connector", code, healthy is null ? "unknown" : healthy.Value ? "healthy" : "unhealthy",
                healthy is { } value ? value ? 1 : 0 : null);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            KafkaMessagingTelemetry.InvalidateConnectorHealth(code);
            _samples.Set("connector", code, "error");
            KafkaMessagingTelemetry.RecordConnectorError("debezium", code, "control_observation_failed");
        }
        if (!routeValidated) { InvalidateStream(binding, "unknown"); _samples.Set("connector", code, "error"); return null; }
        try
        {
            var probe = string.IsNullOrWhiteSpace(retentionId) ? null
                : new KafkaRetentionTimeProbe(retentionId, _options.MaximumBrokerClockSkewSeconds);
            var lag = observation = await _lag.ObserveLagAsync(_kafka, binding.TopicName, binding.ConsumerGroupId, deadline, probe, publishTelemetry: false).ConfigureAwait(false);
            _samples.Set("consumer_lag", code, lag is null ? "unknown" : "healthy", lag?.TotalLagMessages);
            if (lag is null) KafkaMessagingTelemetry.InvalidateConsumerLag(binding.ConsumerGroupId);
            _samples.Set("lag_retention", code, lag is { RetentionTimeFailed: true } ? "error"
                : lag?.LagRetentionRatio is { } ratio ? ratio > 0.8 ? "unhealthy" : "healthy" : "unknown");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            KafkaMessagingTelemetry.InvalidateConsumerLag(binding.ConsumerGroupId);
            _samples.Set("consumer_lag", code, "error"); _samples.Set("lag_retention", code, "unknown");
        }
        try
        {
            var position = routeValidated ? await _connect.TryReadConnectorPositionAsync(binding.ConnectorName, deadline).ConfigureAwait(false) : null;
            CdcSourcePositionHealthSnapshot? source = null;
            var sourceTimeFailed = false;
            if (position is not null)
            {
                await using var scope = _scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                source = await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>()
                    .ReadSourcePositionAsync(binding.SourceDatabaseName, position, deadline).ConfigureAwait(false);
                if (source is { Recoverable: true, SourceLagSeconds: null })
                {
                    try
                    {
                        var timeLag = await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>()
                            .ReadSourceTimeLagAsync(binding.SourceDatabaseName, position, deadline).ConfigureAwait(false);
                        source = source with { SourceLagSeconds = timeLag };
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        // 可选事务时间证据缺失不能污染源文件/LSN 的恢复性结论。
                        KafkaMessagingTelemetry.RecordConnectorError("debezium", code, "source_time_observation_failed");
                        sourceTimeFailed = true;
                    }
                }
            }
            _samples.Set("connector_offset", code, source?.Recoverable is { } valid ? valid ? "healthy" : "unhealthy" : "unknown");
            _samples.Set("connector_lag", code, sourceTimeFailed ? "error"
                : source?.SourceLagSeconds is { } seconds ? seconds > 30 ? "unhealthy" : "healthy" : "unknown");
            KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", code, source?.SourceLagSeconds,
                source?.Recoverable is { } recoverable ? !recoverable : null, TimeSpan.FromSeconds(_options.MaximumSampleAgeSeconds));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            _samples.Set("connector_offset", code, "error");
            _samples.Set("connector_lag", code, "unknown");
            KafkaMessagingTelemetry.InvalidateConnectorHealth(code);
            KafkaMessagingTelemetry.RecordConnectorError("debezium", code, "source_observation_failed");
        }
        return observation;
    }
}
