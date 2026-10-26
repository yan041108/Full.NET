namespace Full.NET.Messaging.Abstractions;

/// <summary>回退控制面日志；资源快照和动作意图必须先于外部调用持久化。</summary>
public interface IEventDeliveryRollbackJournal
{
    /// <summary>读取不可跨代次复用的控制面记录。</summary>
    Task<EventDeliveryRollbackOperation?> FindAsync(Guid generation, CancellationToken cancellationToken = default);

    /// <summary>仅在准备态绑定一次资源快照；已有快照不可覆盖。</summary>
    Task<bool> BindResourcesAsync(Guid generation, string resourcesJson, CancellationToken cancellationToken = default);

    /// <summary>按预期状态迁移，调用方必须持有控制面会话锁。</summary>
    Task<bool> TransitionAsync(Guid generation, EventDeliveryRollbackPhase expected,
        EventDeliveryRollbackPhase next, CancellationToken cancellationToken = default);
}

/// <summary>回退动作的持久状态；未知外部结果保留原意图，恢复时重新观察。</summary>
public enum EventDeliveryRollbackPhase
{
    /// <summary>生产者已冻结，尚未调用外部暂停。</summary>
    Prepared = 0,
    /// <summary>已持久化暂停意图，外部结果可能未知。</summary>
    PauseRequested = 1,
    /// <summary>已确认停止，可进行排空和最终回退。</summary>
    Paused = 2,
    /// <summary>正在恢复原 CDC 链路，禁止最终切换所有者。</summary>
    Recovering = 3,
    /// <summary>已确认原链路运行并解除生产者保护。</summary>
    Recovered = 4,
    /// <summary>已原子提交 Legacy 所有者，禁止恢复原 Connector。</summary>
    Completed = 5,
}

/// <summary>Host 级回退记录，资源快照仅供控制面使用，不向普通查询泄露。</summary>
public sealed record EventDeliveryRollbackOperation(Guid Id, string EventType, int SchemaVersion,
    EventDeliveryRollbackPhase Phase, string? ResourcesJson, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    bool HasPauseIntent = false, Guid? OperatorUserId = null, string? Reason = null);

/// <summary>API 各实例共用的控制面互斥资源；锁不承载数据库事务。</summary>
public static class EventDeliveryControlResources
{
    /// <summary>串行化切流、回退与故障恢复，避免外部动作互相覆盖。</summary>
    public const string SessionLock = "messaging.delivery_control";
}
