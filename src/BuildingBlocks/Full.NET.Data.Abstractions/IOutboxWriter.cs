using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Abstractions;

/// <summary>
/// Outbox 事务原子写入接口：将集成事件追加到与业务命令相同的本地事务中，
/// 保证「业务状态变更 + 事件发布」的原子性，是至少一次投递的基石。
/// </summary>
/// <remarks>
/// <para>
/// 核心安全不变量：
/// <list type="number">
/// <item>
/// AddAsync 必须在调用方已打开的 IDbTransaction 内执行；如果事务不存在或已提交，
/// 实现必须抛出 InvalidOperationException，杜绝事件脱离业务事务独立落库。
/// </item>
/// <item>
/// 事件顺序必须严格等于调用顺序，因为下游 Inbox 幂等去重依赖 MessageId 与
/// OccurredAtUtc 的单调关系。同一事务内按调用序插入可保证 WAL 中的行顺序一致。
/// </item>
/// <item>
/// TenantId、TraceId、OccurredAtUtc 由实现从当前 Ambient 上下文自动提取，
/// 不允许调用方覆盖，防止伪造租户边界或时间回溯。
/// </item>
/// </list>
/// </para>
/// <para>
/// 典型调用时序：BeginTransaction → 业务写 SQL (ICommandExecutor) →
/// IOutboxWriter.AddAsync → CommitTransaction。事务提交后，Outbox Relay
/// （后台 Worker）通过 <see cref="IOutboxStore"/> 领租约并发布到 Broker。
/// </para>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    /// 追加集成事件到当前事务的 Outbox 行，使用默认元数据（由实现从 Ambient 上下文提取）。
    /// </summary>
    /// <typeparam name="TEvent">事件载荷类型，必须为可序列化的 POCO。</typeparam>
    /// <param name="eventType">规范化事件类型名，如 "fullnet.organization.unit.changed"。</param>
    /// <param name="schemaVersion">事件结构版本正整数，用于 Inbox 端路由到对应 Handler。</param>
    /// <param name="payload">事件载荷对象，由 <see cref="IIntegrationEventSerializer"/> 序列化。</param>
    /// <param name="cancellationToken">用于取消当前事务内插入的令牌。</param>
    /// <exception cref="InvalidOperationException">当前调用链上没有已打开的本地命令事务。</exception>
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 追加集成事件到当前事务的 Outbox 行，允许调用方显式覆盖元数据（仅限 Host 级网关场景）。
    /// </summary>
    /// <remarks>
    /// 该重载仅限以下场景使用：
    /// 1) 从外部系统（如 Webhook、Saga Orchestrator）引入的伪事件需要保留原始 TraceId；
    /// 2) 历史数据回填工具需要固定 OccurredAtUtc 以对齐下游幂等去重键。
    /// 普通业务用例必须使用无 metadata 的重载，禁止手动传入 TenantId。
    /// </remarks>
    /// <typeparam name="TEvent">事件载荷类型，必须为可序列化的 POCO。</typeparam>
    /// <param name="eventType">规范化事件类型名。</param>
    /// <param name="schemaVersion">事件结构版本正整数。</param>
    /// <param name="payload">事件载荷对象。</param>
    /// <param name="metadata">显式提供的事件元数据；其中 TenantId 为 null 时仍从上下文自动注入。</param>
    /// <param name="cancellationToken">用于取消当前事务内插入的令牌。</param>
    /// <exception cref="InvalidOperationException">当前调用链上没有已打开的本地命令事务。</exception>
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default);
}
