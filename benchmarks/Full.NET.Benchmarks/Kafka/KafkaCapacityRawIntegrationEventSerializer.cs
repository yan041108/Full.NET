using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// Scope C 容量 Outbox 直接持久化二进制测试信封，避免 MessagePack 二次包装。
/// </summary>
internal sealed class KafkaCapacityRawIntegrationEventSerializer : IIntegrationEventSerializer
{
    /// <summary>
    /// 与 Scope B Kafka Header 一致，满足 Envelope V2 契约校验；负载仍为自定义二进制 Codec。
    /// </summary>
    public string ContentType => MessagingNames.ContentTypeMessagePack;

    public byte[] Serialize<TEvent>(TEvent payload) =>
        payload switch
        {
            byte[] bytes => bytes,
            ReadOnlyMemory<byte> memory => memory.ToArray(),
            _ => throw new NotSupportedException(
                "Kafka capacity outbox only accepts raw byte[] payloads."),
        };

    public TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload) =>
        typeof(TEvent) == typeof(byte[])
            ? (TEvent)(object)payload.ToArray()
            : throw new NotSupportedException(
                "Kafka capacity outbox only supports raw byte[] deserialization.");
}
