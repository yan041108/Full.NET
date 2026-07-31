namespace Full.NET.Realtime;

/// <summary>
/// 业务模块发布实时通知的唯一入口；禁止直接依赖 SignalR Hub 上下文。
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>
    /// 向指定用户组推送消息（组名由 <see cref="RealtimeGroups.User"/> 生成）。
    /// </summary>
    /// <remarks>
    /// 返回成功只表示 SignalR 服务端发送任务完成，不表示客户端已经接收或处理。
    /// </remarks>
    /// <param name="userId">目标用户标识。</param>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向命名组推送消息；组名须遵循平台命名约定并经过授权校验。
    /// </summary>
    /// <remarks>
    /// 返回成功只表示 SignalR 服务端发送任务完成，不表示客户端已经接收或处理。
    /// </remarks>
    /// <param name="groupName">经过调用方授权与规范化的目标组名。</param>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    Task PublishToGroupAsync(
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken = default);
}
