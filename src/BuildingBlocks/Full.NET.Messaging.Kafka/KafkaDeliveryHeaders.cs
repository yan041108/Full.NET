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
        SetHeader(headers, KafkaDeliveryHeaderNames.SourceTopic, consumeResult.Topic);
        SetHeader(headers, KafkaDeliveryHeaderNames.SourcePartition, consumeResult.Partition.Value.ToString(CultureInfo.InvariantCulture));
        SetHeader(headers, KafkaDeliveryHeaderNames.SourceOffset, consumeResult.Offset.Value.ToString(CultureInfo.InvariantCulture));
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

    private static void SetHeader(Headers headers, string key, string value) =>
        headers.Add(key, Encoding.UTF8.GetBytes(value));
}
