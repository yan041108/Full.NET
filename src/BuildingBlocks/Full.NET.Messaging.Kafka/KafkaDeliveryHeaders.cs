using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 读取与写入 Retry/DLQ 元数据头。
/// </summary>
internal static class KafkaDeliveryHeaders
{
    public static int ReadAttemptCount(Headers? headers)
    {
        if (!TryReadHeader(headers, KafkaDeliveryHeaderNames.AttemptCount, out var text)
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count < 0)
        {
            return 0;
        }

        return count;
    }

    public static Headers CloneHeaders(Headers? source)
    {
        var headers = new Headers();
        if (source is null)
        {
            return headers;
        }

        foreach (var header in source)
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        return headers;
    }

    public static void SetRetryNotBeforeUtc(Headers headers, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(headers);
        SetHeader(
            headers,
            KafkaDeliveryHeaderNames.RetryNotBeforeUtc,
            value.ToString("O", CultureInfo.InvariantCulture));
    }

    public static bool TryReadRetryNotBeforeUtc(
        Headers? headers,
        out DateTimeOffset value)
    {
        value = default;
        return TryReadHeader(
                headers,
                KafkaDeliveryHeaderNames.RetryNotBeforeUtc,
                out var text)
            && DateTimeOffset.TryParseExact(
                text,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value);
    }

    public static void ApplyFailureMetadata(
        Headers headers,
        string consumerName,
        ConsumeResult<string, byte[]> consumeResult,
        IntegrationEventFailure failure,
        int attemptCount,
        DateTimeOffset failedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(consumeResult);

        SetHeader(headers, KafkaDeliveryHeaderNames.ConsumerName, consumerName);
        var isRetryHop = KafkaTopicNames.TryGetRetryStage(
            consumeResult.Topic,
            out _);
        SetSourceHeader(
            headers,
            KafkaDeliveryHeaderNames.SourceTopic,
            consumeResult.Topic,
            isRetryHop);
        SetSourceHeader(
            headers,
            KafkaDeliveryHeaderNames.SourcePartition,
            consumeResult.Partition.Value.ToString(CultureInfo.InvariantCulture),
            isRetryHop);
        SetSourceHeader(
            headers,
            KafkaDeliveryHeaderNames.SourceOffset,
            consumeResult.Offset.Value.ToString(CultureInfo.InvariantCulture),
            isRetryHop);
        SetHeader(headers, KafkaDeliveryHeaderNames.AttemptCount, attemptCount.ToString(CultureInfo.InvariantCulture));
        SetHeader(headers, KafkaDeliveryHeaderNames.FailureCode, failure.Code);
        SetHeader(headers, KafkaDeliveryHeaderNames.FailureKind, failure.Kind.ToString());
        SetHeader(headers, KafkaDeliveryHeaderNames.FailureSummary, failure.Summary);
        SetHeader(headers, KafkaDeliveryHeaderNames.LastFailedAtUtc, failedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        if (!TryReadHeader(headers, KafkaDeliveryHeaderNames.FirstFailedAtUtc, out _))
        {
            SetHeader(headers, KafkaDeliveryHeaderNames.FirstFailedAtUtc, failedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    public static bool TryReadHeader(Headers? headers, string key, out string value)
    {
        value = string.Empty;
        if (headers is null)
        {
            return false;
        }

        var header = headers.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        if (header is null || header.GetValueBytes() is null)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(header.GetValueBytes());
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        // Kafka 允许同名 Header 重复；重试链若只追加，会一直读到第一次尝试的旧值，
        // 从而重复进入同一 Retry Topic 并绕过后续退避阶段。
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static void SetSourceHeader(
        Headers headers,
        string key,
        string value,
        bool isRetryHop)
    {
        // 正式 Topic 是可信追溯起点，必须覆盖外来同名 Header；进入 Retry 链后只在
        // 缺失时补齐，避免最终 DLQ 只剩最后一跳的 Topic/Partition/Offset。
        if (!isRetryHop || !TryReadHeader(headers, key, out _))
        {
            SetHeader(headers, key, value);
        }
    }
}
