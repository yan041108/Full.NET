namespace Full.NET.Realtime;

/// <summary>
/// Realtime 关闭时的空实现，避免业务模块分支判断发布器是否存在。
/// </summary>
/// <remarks>
/// 该实现静默丢弃所有消息并立即返回完成，仅用于开发、测试或显式禁用实时推送的部署；
/// 不得在生产拓扑中作为降级目标，否则会掩盖实时通道断开造成的业务影响。
/// </remarks>
public sealed class NullRealtimePublisher : IRealtimePublisher
{
    /// <summary>
    /// 单例实例；DI 容器在 Realtime 未启用时返回该实例。
    /// </summary>
    public static NullRealtimePublisher Instance { get; } = new();

    private NullRealtimePublisher()
    {
    }

    /// <summary>
    /// 静默丢弃向指定用户推送的消息，立即返回完成。
    /// </summary>
    /// <param name="userId">目标用户标识；本实现不读取，仅用于保持契约签名。</param>
    /// <param name="message">待发布的消息；本实现不读取，也不进行日志或审计记录。</param>
    /// <param name="cancellationToken">取消令牌；本实现不进行任何 IO，故忽略取消请求。</param>
    /// <returns>始终返回已完成的 <see cref="Task"/>，不产生任何副作用。</returns>
    public Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// 静默丢弃向指定租户广播组推送的消息，立即返回完成。
    /// </summary>
    /// <param name="tenantId">目标租户标识；本实现不读取，仅用于保持契约签名。</param>
    /// <param name="message">待发布的消息；本实现不读取，也不进行日志或审计记录。</param>
    /// <param name="cancellationToken">取消令牌；本实现不进行任何 IO，故忽略取消请求。</param>
    /// <returns>始终返回已完成的 <see cref="Task"/>，不产生任何副作用。</returns>
    public Task PublishToTenantAsync(
        Guid tenantId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// 静默丢弃向 Host 广播组推送的消息，立即返回完成。
    /// </summary>
    /// <param name="message">待发布的消息；本实现不读取，也不进行日志或审计记录。</param>
    /// <param name="cancellationToken">取消令牌；本实现不进行任何 IO，故忽略取消请求。</param>
    /// <returns>始终返回已完成的 <see cref="Task"/>，不产生任何副作用。</returns>
    public Task PublishToHostBroadcastAsync(
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
