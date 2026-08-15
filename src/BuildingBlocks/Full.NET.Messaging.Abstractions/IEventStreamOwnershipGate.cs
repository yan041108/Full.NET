namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 在业务 Outbox 写入与事件流所有权切换之间建立事务级读写互斥。
/// 锁必须由数据库事务持有到提交或回滚，调用方不得提前释放。
/// 三种互斥角色分别对应：生产者写入 Outbox、消费者读取 Inbox、运维/管理端变更所有权。
/// </summary>
public interface IEventStreamOwnershipGate
{
    /// <summary>
    /// 获取生产者侧事务级排它锁；防止切流过程中 Legacy 与 CDC 同时向同一事件流写入 Outbox 记录。
    /// </summary>
    /// <param name="eventType">事件契约类型名，与 <see cref="EventStreamOwnershipRecord.MessageType"/> 对齐。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>获取成功返回 true；若存在并发所有权变更事务在途则返回 false，业务应中止当前请求。</returns>
    Task<bool> AcquireProducerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取消费者侧共享读锁；防止消费者在所有权变更提交期间读取到半切换状态的旧 Owner 记录。
    /// </summary>
    /// <param name="eventType">事件契约类型名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>锁定成功返回 true；否则消费者需暂停该事件流的后续拉取。</returns>
    Task<bool> AcquireConsumerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一次事务锁定查询中返回 Consumer 可见的当前 Owner。
    /// 默认结果表示旧实现不支持该优化，调用方必须回退到原 Gate 与 Resolver 组合。
    /// </summary>
    /// <param name="eventType">事件契约类型名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 包含是否支持优化、记录是否存在以及锁定快照中可见的 <see cref="EventDeliveryOwner"/>。
    /// </returns>
    Task<EventStreamConsumerFenceResult> AcquireConsumerFenceAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EventStreamConsumerFenceResult.Unsupported);

    /// <summary>
    /// 获取所有权变更的独占写锁；与 Producer/Consumer 角色互斥，确保切流事务在串行条件下提交。
    /// </summary>
    /// <param name="eventType">事件契约类型名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功获取返回 true；若业务仍在写入或消费该事件流则返回 false，需稍后重试。</returns>
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
