using System.Globalization;
using Confluent.Kafka;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>独立消费已登记订阅的 DLQ；投影提交后确认 Offset，不参与业务消费组水位。</summary>
internal sealed class KafkaDeadLetterProjectionWorker(IOptions<KafkaMessagingOptions> options,
    IServiceScopeFactory scopeFactory, KafkaEnvelopeReader reader,
    ILogger<KafkaDeadLetterProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topics = await ReadTopicsAsync();
        if (topics.Length == 0) return;

        using var consumer = new ConsumerBuilder<string, byte[]>(
            options.Value.BuildConsumerConfig(options.Value.DeadLetterProjectionGroupId)).Build();
        consumer.Subscribe(topics);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? message = null;
                try
                {
                    message = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    if (message?.Message is null) continue;
                    await using var scope = scopeFactory.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                    var sink = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterSink>();
                    await sink.CaptureAsync(KafkaDeadLetterCapture.Create(message, reader), stoppingToken);
                    // 数据库提交与 Broker 确认之间崩溃允许重复读取；位置唯一键确保投影不被覆盖。
                    consumer.Commit(message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogWarning("DLQ 投影未确认消息，异常类型 {FailureType}；稍后重试。", exception.GetType().Name);
                    if (message is not null)
                    {
                        try { consumer.Seek(message.TopicPartitionOffset); }
                        catch (KafkaException) { /* 重平衡已撤销分区时依靠未提交 Offset 重新领取。 */ }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        finally { consumer.Close(); }
    }

    private async Task<string[]> ReadTopicsAsync()
    {
        // 订阅可依赖仅实现 IAsyncDisposable 的数据库会话，目录探测也必须异步释放作用域。
        await using var scope = scopeFactory.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        return scope.ServiceProvider.GetServices<IIntegrationEventSubscription>()
            .Select(s => KafkaTopicNames.GetDeadLetterTopic(catalog.GetTopicRequired(s.EventType, s.SchemaVersion).TopicCode))
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>把 Kafka DLQ 记录转换为静态持久契约；损坏信封保留定位而不允许执行。</summary>
internal static class KafkaDeadLetterCapture
{
    private static readonly HashSet<string> EnvelopeHeaders = new(StringComparer.Ordinal)
    {
        KafkaEnvelopeHeaderNames.EventId, KafkaEnvelopeHeaderNames.MessageType, KafkaEnvelopeHeaderNames.SchemaVersion,
        KafkaEnvelopeHeaderNames.ContentType, KafkaEnvelopeHeaderNames.TenantId, KafkaEnvelopeHeaderNames.CorrelationId,
        KafkaEnvelopeHeaderNames.CausationId, KafkaEnvelopeHeaderNames.TraceParent, KafkaEnvelopeHeaderNames.Producer,
        KafkaEnvelopeHeaderNames.OccurredAtUtc,
    };

    public static IntegrationEventDeadLetterCapture Create(ConsumeResult<string, byte[]> message, KafkaEnvelopeReader reader)
    {
        var headers = message.Message.Headers ?? new Headers();
        var valid = reader.TryRead(message, out var envelope, out var invalidCode);
        // 管理投影的列边界不能让一条异常元数据永久堵塞整个 DLQ 分区。
        if (envelope is not null && (envelope.MessageType.Length > 256 || !IsAscii(envelope.MessageType)))
        {
            valid = false;
            envelope = null;
            invalidCode = "messaging.dead_letter.metadata_invalid";
        }
        var consumer = Header(headers, KafkaDeliveryHeaderNames.ConsumerName, "unknown", 200);
        var sourceTopic = Header(headers, KafkaDeliveryHeaderNames.SourceTopic, message.Topic, 249);
        var partition = Number(headers, KafkaDeliveryHeaderNames.SourcePartition, message.Partition.Value);
        var offset = Number(headers, KafkaDeliveryHeaderNames.SourceOffset, message.Offset.Value);
        var now = DateTimeOffset.UtcNow;
        var first = Time(headers, KafkaDeliveryHeaderNames.FirstFailedAtUtc, now);
        var last = Time(headers, KafkaDeliveryHeaderNames.LastFailedAtUtc, first);
        var snapshot = new DeadLetterMessageSnapshot(valid ? null : message.Message.Key,
            valid ? null : message.Message.Value, valid ? [] : headers
                .Where(h => EnvelopeHeaders.Contains(h.Key))
                .Select(h => new DeadLetterMessageHeader(h.Key, h.GetValueBytes())).ToArray(),
            envelope is null ? null : DeadLetterReplayEnvelope.FromEnvelope(envelope));
        return new IntegrationEventDeadLetterCapture(Guid.CreateVersion7(), consumer, message.Topic,
            message.Partition.Value, message.Offset.Value, sourceTopic, checked((int)partition), offset,
            envelope?.EventId, envelope?.MessageType, envelope?.SchemaVersion, envelope?.TenantId,
            KafkaDeliveryHeaders.ReadAttemptCount(headers),
            Header(headers, KafkaDeliveryHeaderNames.FailureCode, invalidCode ?? "messaging.dead_letter.unknown", 128),
            first, last, valid && consumer != "unknown", snapshot.ToJson());
    }

    private static string Header(Headers headers, string key, string fallback, int limit) =>
        KafkaDeliveryHeaders.TryReadHeader(headers, key, out var value) && value.Length <= limit && IsAscii(value) ? value : fallback;

    private static bool IsAscii(string value) => value.All(c => c is >= ' ' and <= '~');

    private static long Number(Headers headers, string key, long fallback) =>
        KafkaDeliveryHeaders.TryReadHeader(headers, key, out var value)
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 0 && (key != KafkaDeliveryHeaderNames.SourcePartition || number <= int.MaxValue) ? number : fallback;

    private static DateTimeOffset Time(Headers headers, string key, DateTimeOffset fallback)
    {
        if (!KafkaDeliveryHeaders.TryReadHeader(headers, key, out var value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            return fallback;
        var utc = time.ToUniversalTime();
        // MySQL datetime 下界按转换后的 UTC 检查，防止正偏移把合法本地年份移到 999 年并堵塞投影。
        return utc.Year >= 1000 ? utc : fallback;
    }
}
