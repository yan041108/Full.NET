using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// 事件流所有权并发冲突：执行基于 PreviousOwner 的乐观并发控制 (CAS) 时，
/// 数据库中实际的 CurrentOwner 与期望不匹配，表示在读取当前所有权和写入
/// 新所有权之间有另一事务已经成功切流。调用方应捕获并翻译成 conflict。
/// </summary>
public sealed class EventStreamOwnershipConcurrencyException : Exception
{
    public EventStreamOwnershipConcurrencyException(
        string messageType,
        int schemaVersion,
        EventDeliveryOwner expectedOwner,
        EventDeliveryOwner actualOwner)
        : base(
            $"Event stream ownership CAS failed for '{messageType}' schema {schemaVersion}. " +
            $"Expected CurrentOwner={expectedOwner} but database CurrentOwner={actualOwner}.")
    {
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        ExpectedOwner = expectedOwner;
        ActualOwner = actualOwner;
    }

    /// <summary>
    /// 发生 CAS 冲突的事件消息类型全名；用于定位是哪条事件流的所有权切换失败。
    /// </summary>
    public string MessageType { get; }

    /// <summary>
    /// 发生 CAS 冲突的事件 Schema 版本号。
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// 期望的当前所有权归属；调用方在写入新所有权前读取到的值。
    /// </summary>
    public EventDeliveryOwner ExpectedOwner { get; }

    /// <summary>
    /// 数据库中实际的当前所有权归属；与 <see cref="ExpectedOwner"/> 不一致表示已被另一事务抢先切流。
    /// </summary>
    public EventDeliveryOwner ActualOwner { get; }
}
