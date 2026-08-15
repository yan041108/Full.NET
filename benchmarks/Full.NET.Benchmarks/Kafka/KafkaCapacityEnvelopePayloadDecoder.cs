using System.Text.Json;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 从 Kafka 记录值解码容量测试信封，兼容 Debezium 路由后的二进制与 Connect JSON 包装。
/// </summary>
internal static class KafkaCapacityEnvelopePayloadDecoder
{
    public static bool TryDecode(
        ReadOnlySpan<byte> value,
        out KafkaCapacityEnvelope envelope)
    {
        if (KafkaCapacityEnvelopeCodec.TryDecode(value, out envelope))
        {
            return true;
        }

        if (TryDecodeConnectJsonPayload(value, out var payload)
            && KafkaCapacityEnvelopeCodec.TryDecode(payload, out envelope))
        {
            return true;
        }

        envelope = default!;
        return false;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> payload,
        out KafkaCapacityEnvelope envelope) =>
        TryDecode(payload.Span, out envelope);

    public static bool TryUnwrapEnvelopeBytes(
        ReadOnlySpan<byte> value,
        out byte[] envelopeBytes)
    {
        if (KafkaCapacityEnvelopeCodec.TryDecode(value, out _))
        {
            envelopeBytes = value.ToArray();
            return true;
        }

        if (TryDecodeConnectJsonPayload(value, out envelopeBytes)
            && KafkaCapacityEnvelopeCodec.TryDecode(envelopeBytes, out _))
        {
            return true;
        }

        envelopeBytes = [];
        return false;
    }

    private static bool TryDecodeConnectJsonPayload(
        ReadOnlySpan<byte> value,
        out byte[] payload)
    {
        payload = [];
        if (value.IsEmpty)
        {
            return false;
        }

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            if (element.ValueKind == JsonValueKind.String)
            {
                payload = element.GetBytesFromBase64();
                return payload.Length > 0;
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("payload", out var payloadElement))
            {
                if (payloadElement.ValueKind == JsonValueKind.String)
                {
                    payload = payloadElement.GetBytesFromBase64();
                    return payload.Length > 0;
                }

                if (payloadElement.ValueKind == JsonValueKind.Array)
                {
                    return TryDecodeJsonByteArray(payloadElement, out payload);
                }
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                return TryDecodeJsonByteArray(element, out payload);
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
