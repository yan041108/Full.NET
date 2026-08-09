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

    public string MessageType { get; }

    public int SchemaVersion { get; }

    public EventDeliveryOwner ExpectedOwner { get; }

    public EventDeliveryOwner ActualOwner { get; }
}
