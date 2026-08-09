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

    /// <summary>
    /// 在同一次事务锁定查询中返回 Consumer 可见的当前 Owner。
    /// 默认结果表示旧实现不支持该优化，调用方必须回退到原 Gate 与 Resolver 组合。
    /// </summary>
    Task<EventStreamConsumerFenceResult> AcquireConsumerFenceAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EventStreamConsumerFenceResult.Unsupported);

    Task<bool> AcquireOwnershipChangeAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumer 所有权 Fence 的锁定读取结果；区分“不支持优化”与“数据库不存在该事件流”。
/// </summary>
public readonly record struct EventStreamConsumerFenceResult(
    bool IsSupported,
    bool OwnershipExists,
    EventDeliveryOwner? CurrentOwner)
{
    public static EventStreamConsumerFenceResult Unsupported { get; } =
        new(IsSupported: false, OwnershipExists: false, CurrentOwner: null);

    public static EventStreamConsumerFenceResult Missing { get; } =
        new(IsSupported: true, OwnershipExists: false, CurrentOwner: null);

    public static EventStreamConsumerFenceResult Acquired(EventDeliveryOwner currentOwner)
    {
        if (!Enum.IsDefined(currentOwner))
        {
            throw new ArgumentOutOfRangeException(nameof(currentOwner));
        }

        return new EventStreamConsumerFenceResult(
            IsSupported: true,
            OwnershipExists: true,
            CurrentOwner: currentOwner);
    }
}
