using System.Diagnostics.CodeAnalysis;
using System.IO;
using Full.NET.Data.Abstractions;
using global::MemoryPack;

namespace Full.NET.Serialization.MemoryPack;

/// <summary>
/// 以 MemoryPack 序列化 Integration Event 的稳定实现；用于 Outbox 与跨服务二进制载荷交换。
/// </summary>
/// <remarks>
/// <para>事件类型须标注 <c>[MemoryPackable]</c> 并声明为 <c>partial</c>，由源生成器产出 AOT 友好格式化器；
/// 须遵守 ADR-0008 §4.6 受控二进制协议：仅具体 DTO、禁止 Union/接口/object 多态，路由由
/// <c>IntegrationEventEnvelope.MessageType</c> 承担。</para>
/// <para>载荷仍应作为不可信输入处理：消费方须校验字段长度、枚举范围与租户边界，序列化器本身不做业务语义校验。</para>
/// <para><see cref="ContentType"/> 为稳定机器契约，不可本地化；该序列化器不应通过公共 Web API 直接暴露二进制格式。</para>
/// </remarks>
public sealed class MemoryPackIntegrationEventSerializer : IIntegrationEventSerializer
{
    /// <summary>
    /// 该序列化器产出的 Content-Type，用于 Outbox 载荷标识与消费方分发。
    /// </summary>
    public string ContentType => "application/x-memorypack";

    /// <summary>
    /// 将事件实例序列化为 MemoryPack 二进制载荷。
    /// </summary>
    /// <typeparam name="TEvent">事件类型，必须已用 MemoryPack 特性声明并可被源生成器处理。</typeparam>
    /// <param name="payload">事件实例；不可为 <c>null</c>。</param>
    /// <returns>MemoryPack 二进制字节；语义等价对象应产出相同字节以支持 Inbox PayloadHash 幂等。</returns>
    public byte[] Serialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent payload) =>
        MemoryPackSerializer.Serialize(payload);

    /// <summary>
    /// 将 MemoryPack 二进制载荷反序列化为事件实例。
    /// </summary>
    /// <typeparam name="TEvent">目标事件类型；消费方必须持有与生产方兼容的契约版本。</typeparam>
    /// <param name="payload">MemoryPack 二进制载荷；可能来自不可信来源。</param>
    /// <returns>反序列化得到的事件实例。</returns>
    public TEvent Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        ReadOnlyMemory<byte> payload)
    {
        try
        {
            return MemoryPackSerializer.Deserialize<TEvent>(payload.Span)!;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Outbox 死信路径将 InvalidDataException 视为不可重试的载荷损坏。
            throw new InvalidDataException("MemoryPack payload is invalid.", exception);
        }
    }
}
