namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 处理按消息类型和模式版本精确路由的 Integration Event。
/// </summary>
public interface IIntegrationEventHandler
{
    /// <summary>获取规范消息类型。</summary>
    string EventType { get; }

    /// <summary>获取迁移窗口内仍可消费的历史消息类型。</summary>
    IReadOnlyList<string> LegacyEventTypes => [];

    /// <summary>获取该 Handler 支持的载荷模式版本。</summary>
    int SchemaVersion { get; }

    /// <summary>
    /// 获取至少一次投递下的幂等策略；默认值仅用于兼容旧实现，Worker 启动时会拒绝它。
    /// </summary>
    IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.Unspecified;

    /// <summary>
    /// 使用完整消息上下文处理载荷；旧实现默认转发至 payload-only 重载。
    /// </summary>
    /// <param name="context">包含稳定 MessageId、租户、追踪和事件时间的投递上下文。</param>
    /// <param name="payload">原始 MemoryPack 载荷。</param>
    /// <param name="cancellationToken">宿主退出或租约续期失败时触发的取消令牌。</param>
    Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        HandleAsync(payload, cancellationToken);

    /// <summary>
    /// 处理原始 MemoryPack 载荷；保留此重载以兼容尚未读取消息上下文的 Handler。
    /// </summary>
    /// <param name="payload">原始 MemoryPack 载荷。</param>
    /// <param name="cancellationToken">宿主退出或租约续期失败时触发的取消令牌。</param>
    Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}
