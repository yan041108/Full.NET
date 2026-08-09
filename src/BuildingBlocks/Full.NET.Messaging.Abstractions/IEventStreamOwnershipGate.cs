namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 在业务 Outbox 写入与事件流所有权切换之间建立事务级读写互斥。
/// 锁必须由数据库事务持有到提交或回滚，调用方不得提前释放。
/// </summary>
public interface IEventStreamOwnershipGate
{
    Task<bool> AcquireProducerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    Task<bool> AcquireConsumerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    Task<bool> AcquireOwnershipChangeAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);
}
