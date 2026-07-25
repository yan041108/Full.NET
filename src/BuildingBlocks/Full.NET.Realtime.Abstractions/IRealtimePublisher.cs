namespace Full.NET.Realtime;

/// <summary>
/// 业务模块发布实时通知的唯一入口；禁止直接依赖 SignalR Hub 上下文。
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>向指定用户组推送消息（组名由 <see cref="RealtimeGroups.User"/> 生成）。</summary>
    Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>向命名组推送消息；组名须遵循平台命名约定并经过授权校验。</summary>
    Task PublishToGroupAsync(
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken = default);
}
