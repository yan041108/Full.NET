namespace Full.NET.Realtime;

/// <summary>
/// 业务模块发布实时通知的唯一入口；禁止直接依赖 SignalR Hub 上下文。
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>通知账号的所有连接清理指定会话；服务端撤销独立生效，不信任客户端执行。</summary>
    /// <param name="userId">会话所属用户。</param>
    /// <param name="sessionId">已在权威源撤销的会话。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishSessionRevokedAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

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
    /// 向当前租户的广播组推送消息。
    /// </summary>
    /// <remarks>
    /// 返回成功只表示 SignalR 服务端发送任务完成，不表示客户端已经接收或处理。
    /// </remarks>
    /// <param name="tenantId">目标租户标识，必须与当前租户上下文一致。</param>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    Task PublishToTenantAsync(
        Guid tenantId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向 Host 广播组推送消息。
    /// </summary>
    /// <remarks>
    /// 仅允许在明确的 Host 上下文调用；返回成功只表示 SignalR 服务端发送任务完成，
    /// 不表示客户端已经接收或处理。
    /// </remarks>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    Task PublishToHostBroadcastAsync(
        RealtimeMessage message,
        CancellationToken cancellationToken = default);
}
