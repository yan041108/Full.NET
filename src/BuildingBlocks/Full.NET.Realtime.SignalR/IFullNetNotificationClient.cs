namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 管理端通知 Hub 的强类型客户端契约。
/// </summary>
public interface IFullNetNotificationClient
{
    /// <summary>
    /// 向已通过授权的管理端连接推送一条实时消息。
    /// </summary>
    /// <param name="message">已完成服务端裁剪和租户边界过滤的通知载荷。</param>
    Task ReceiveMessageAsync(RealtimeMessage message);
}
