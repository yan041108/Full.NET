using Full.NET.Data.Abstractions;
using global::MessagePack;

namespace Full.NET.Serialization.MessagePack;

/// <summary>
/// 以 MessagePack 序列化 Integration Event 的稳定实现；用于 Outbox 与跨服务二进制载荷交换。
/// </summary>
/// <remarks>
/// <para>使用 <see cref="MessagePackSerializerOptions.Standard"/> 契约解析器，要求事件类型以
/// <c>[MessagePackObject]</c> 与 <c>[Key]</c> 显式标注键名，避免依赖字段顺序造成版本漂移。</para>
/// <para>通过 <see cref="MessagePackSecurity.UntrustedData"/> 启用安全模式，禁用自动反序列化委托与未知类型构造，
/// 防止不可信载荷在反序列化时执行任意代码。事件载荷仍应作为不可信输入处理，字段长度与语义边界须在消费方校验。</para>
/// <para><see cref="ContentType"/> 为稳定机器契约，不可本地化；该序列化器不应通过公共 Web API 直接暴露二进制格式。</para>
/// </remarks>
public sealed class MessagePackIntegrationEventSerializer : IIntegrationEventSerializer
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>
    /// 该序列化器产出的 Content-Type，用于 Outbox 载荷标识与消费方分发。
    /// </summary>
    public string ContentType => "application/x-msgpack";

    /// <summary>
    /// 将事件实例序列化为 MessagePack 二进制载荷。
    /// </summary>
    /// <typeparam name="TEvent">事件类型，必须已用 MessagePack 特性声明稳定键名。</typeparam>
    /// <param name="payload">事件实例；不可为 <c>null</c>，否则由 MessagePack 抛出原异常。</param>
    /// <returns>MessagePack 二进制字节；调用方负责持久化与版本兼容。</returns>
    public byte[] Serialize<TEvent>(TEvent payload) =>
        MessagePackSerializer.Serialize(payload, SerializerOptions);

    /// <summary>
    /// 将 MessagePack 二进制载荷反序列化为事件实例。
    /// </summary>
    /// <typeparam name="TEvent">目标事件类型；消费方必须持有与生产方兼容的契约版本。</typeparam>
    /// <param name="payload">MessagePack 二进制载荷；可能来自不可信来源，已由安全模式限制反序列化行为。</param>
    /// <returns>反序列化得到的事件实例。</returns>
    public TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<TEvent>(payload, SerializerOptions);
}
