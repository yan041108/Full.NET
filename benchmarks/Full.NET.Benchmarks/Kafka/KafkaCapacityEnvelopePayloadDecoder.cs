using System.Text.Json;
using Full.NET.Serialization.MemoryPack;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 从 Kafka 记录值解码容量测试信封，兼容 Debezium 路由后的二进制与 Connect JSON 包装。
/// </summary>
internal static class KafkaCapacityEnvelopePayloadDecoder
{
    private static readonly MemoryPackIntegrationEventSerializer MemoryPackSerializer = new();

    /// <summary>
    /// 从 Kafka 消息正文读取容量测试信封。
    /// </summary>
    /// <param name="value">Kafka 消息正文。</param>
    /// <param name="envelope">成功时返回解码后的容量测试信封。</param>
    /// <returns>正文包含有效容量测试信封时返回真。</returns>
    public static bool TryDecode(
        ReadOnlySpan<byte> value,
        out KafkaCapacityEnvelope envelope)
    {
        if (TryUnwrapEnvelopeBytes(value, out var envelopeBytes)
            && KafkaCapacityEnvelopeCodec.TryDecode(envelopeBytes, out envelope))
        {
            return true;
        }

        envelope = default!;
        return false;
    }

    /// <summary>
    /// 从只读内存形式的 Kafka 消息正文读取容量测试信封。
    /// </summary>
    /// <param name="payload">Kafka 消息正文。</param>
    /// <param name="envelope">成功时返回解码后的容量测试信封。</param>
    /// <returns>正文包含有效容量测试信封时返回真。</returns>
    public static bool TryDecode(
        ReadOnlyMemory<byte> payload,
        out KafkaCapacityEnvelope envelope) =>
        TryDecode(payload.Span, out envelope);

    /// <summary>
    /// 依次解开 Connect JSON 与生产 MemoryPack 外层，返回容量测试信封的原始字节。
    /// </summary>
    /// <param name="value">Kafka 消息正文。</param>
    /// <param name="envelopeBytes">成功时返回容量测试信封的原始字节。</param>
    /// <returns>正文可解开且内部信封完整时返回真。</returns>
    public static bool TryUnwrapEnvelopeBytes(
        ReadOnlySpan<byte> value,
        out byte[] envelopeBytes)
    {
        if (TryUnwrapSerializedCandidate(value, out envelopeBytes))
        {
            return true;
        }

        // Debezium JSON Converter 会先把数据库二进制列编码成 JSON；生产 Worker 又会
        // 在数据库写入前套一层 MemoryPack，因此需要按实际链路顺序连续解开两层。
        if (TryDecodeConnectJsonPayload(value, out var connectPayload)
            && TryUnwrapSerializedCandidate(connectPayload, out envelopeBytes))
        {
            return true;
        }

        envelopeBytes = [];
        return false;
    }

    /// <summary>
    /// 识别原始容量信封或仅带生产 MemoryPack 外层的候选正文。
    /// </summary>
    /// <param name="value">已经移除 Connect JSON 外层的候选正文。</param>
    /// <param name="envelopeBytes">成功时返回容量测试信封的原始字节。</param>
    /// <returns>候选正文包含完整容量信封时返回真。</returns>
    private static bool TryUnwrapSerializedCandidate(
        ReadOnlySpan<byte> value,
        out byte[] envelopeBytes)
    {
        if (KafkaCapacityEnvelopeCodec.TryDecode(value, out _))
        {
            envelopeBytes = value.ToArray();
            return true;
        }

        if (TryDecodeMemoryPackByteArrayPayload(value, out envelopeBytes)
            && KafkaCapacityEnvelopeCodec.TryDecode(envelopeBytes, out _))
        {
            return true;
        }

        envelopeBytes = [];
        return false;
    }

    /// <summary>
    /// 解开生产 Worker 使用的 MemoryPack 字节数组外层，恢复容量测试的内部二进制信封。
    /// </summary>
    /// <param name="value">Outbox 经生产序列化器写入的消息正文。</param>
    /// <param name="payload">成功时返回内部容量信封字节。</param>
    /// <returns>正文是有效的非空 MemoryPack 字节数组时返回真。</returns>
    private static bool TryDecodeMemoryPackByteArrayPayload(
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
            // 容量信封在 WorkerParity 下先作为 byte[] 经过生产 MemoryPack 序列化；
            // 此处只解开该确定性外层，内部信封仍由容量 Codec 完整校验。
            payload = MemoryPackSerializer.Deserialize<byte[]>(value.ToArray());
            return payload is { Length: > 0 };
        }
        catch (InvalidDataException)
        {
            payload = [];
            return false;
        }
    }

    /// <summary>
    /// 解开 Kafka Connect JSON Converter 对数据库二进制列生成的 JSON 表示。
    /// </summary>
    /// <param name="value">Kafka Connect 输出的候选 JSON 正文。</param>
    /// <param name="payload">成功时返回 JSON 内承载的二进制数据。</param>
    /// <returns>正文是受支持的 JSON 二进制表示时返回真。</returns>
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

    /// <summary>
    /// 将 JSON 数字数组安全转换为字节数组。
    /// </summary>
    /// <param name="element">待解析的 JSON 数组。</param>
    /// <param name="payload">成功时返回转换后的字节数组。</param>
    /// <returns>每个数组元素均为有效字节时返回真。</returns>
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
