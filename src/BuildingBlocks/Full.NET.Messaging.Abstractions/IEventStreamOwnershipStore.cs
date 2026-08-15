namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 读取与写入事件流交付所有权持久化记录。
/// 所有写操作必须与 <see cref="IEventStreamOwnershipGate"/> 在同一数据库事务中调用，
/// 避免读取快照与实际提交状态之间出现不一致窗口。
/// </summary>
public interface IEventStreamOwnershipStore
{
    /// <summary>
    /// 按事件契约查找对应的所有权记录。
    /// </summary>
    /// <param name="messageType">稳定事件类型名。</param>
    /// <param name="schemaVersion">Schema 版本号，从 1 开始。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>找到的所有权记录；若从未配置过返回 null，调用方应按默认 <see cref="EventDeliveryOwner.LegacyPolling"/> 继续。</returns>
    Task<EventStreamOwnershipRecord?> FindAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出全部已登记的事件流所有权，用于管理端切流看板、批量状态查询与运维审计导出。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前持久化层中保存的全部所有权快照。</returns>
    Task<IReadOnlyList<EventStreamOwnershipRecord>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新所有权记录；实现层需对 <see cref="EventStreamOwnershipRecord.UpdatedAtUtc"/> 执行乐观并发检查，
    /// 若记录已被其他事务修改则抛错交由上层重试或拒绝。
    /// </summary>
    /// <param name="record">要写入的完整记录快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UpsertAsync(
        EventStreamOwnershipRecord record,
        CancellationToken cancellationToken = default);
}
