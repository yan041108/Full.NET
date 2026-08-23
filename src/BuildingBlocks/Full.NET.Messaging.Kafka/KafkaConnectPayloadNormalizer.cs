using System.Text.Json;
using Full.NET.Messaging.Kafka.Serialization;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 将 Debezium Outbox EventRouter 经 Connect 投递的二进制 Payload 还原为原始字节。
/// MySQL BLOB 在部分 Connect 配置下会以 JSON/base64 包装出现在 Kafka record value 中。
/// </summary>
internal static class KafkaConnectPayloadNormalizer
{
    internal static ReadOnlyMemory<byte> Normalize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty || payload.Span[0] != (byte)'{')
        {
            return payload;
        }

        return TryUnwrapConnectJson(payload.Span, out var unwrapped)
            ? unwrapped
            : payload;
    }

    private static bool TryUnwrapConnectJson(ReadOnlySpan<byte> value, out ReadOnlyMemory<byte> payload)
    {
        payload = ReadOnlyMemory<byte>.Empty;
        try
        {
            var element = JsonSerializer.Deserialize(
                value,
                KafkaMessagingJsonSerializerContext.Default.JsonElement);
            if (element.ValueKind == JsonValueKind.String)
            {
                payload = element.GetBytesFromBase64();
                return !payload.IsEmpty;
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("payload", out var payloadElement))
            {
                if (payloadElement.ValueKind == JsonValueKind.String)
                {
                    payload = payloadElement.GetBytesFromBase64();
                    return !payload.IsEmpty;
                }

                if (payloadElement.ValueKind == JsonValueKind.Array
                    && TryDecodeJsonByteArray(payloadElement, out var bytes))
                {
                    payload = bytes;
                    return true;
                }
            }

            if (element.ValueKind == JsonValueKind.Array
                && TryDecodeJsonByteArray(element, out var arrayBytes))
            {
                payload = arrayBytes;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static bool TryDecodeJsonByteArray(JsonElement element, out byte[] payload)
    {
        payload = [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var bytes = new byte[element.GetArrayLength()];
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetByte(out bytes[index]))
            {
                return false;
            }

            index++;
        }

        payload = bytes;
        return true;
    }
}
