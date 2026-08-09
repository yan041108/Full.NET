using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Debezium Outbox 路由后的 Kafka Header/Key 解析辅助。
/// </summary>
internal static class KafkaEnvelopeHeaderParsers
{
    internal static bool TryParsePartitionKey(string? keyText, out string partitionKey)
    {
        partitionKey = string.Empty;
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        if (!keyText.StartsWith("{", StringComparison.Ordinal))
        {
            partitionKey = keyText;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(keyText);
            if (!document.RootElement.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            partitionKey = payload.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(partitionKey);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParseGuidHeader(string text, out Guid guid)
    {
        if (Guid.TryParse(text, out guid))
        {
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length != 16)
            {
                guid = default;
                return false;
            }

            guid = new Guid(bytes, bigEndian: true);
            return true;
        }
        catch (FormatException)
        {
            guid = default;
            return false;
        }
    }

    internal static bool TryParseOccurredAtUtc(string text, out DateTimeOffset occurredAtUtc)
    {
        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out occurredAtUtc))
        {
            return true;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            occurredAtUtc = default;
            return false;
        }

        if (raw > 1_000_000_000_000_000L)
        {
            var seconds = raw / 1_000_000L;
            var micros = raw % 1_000_000L;
            occurredAtUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(micros * 10L);
            return true;
        }

        if (raw > 1_000_000_000_000L)
        {
            occurredAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(raw);
            return true;
        }

        occurredAtUtc = DateTimeOffset.FromUnixTimeSeconds(raw);
        return true;
    }
}
