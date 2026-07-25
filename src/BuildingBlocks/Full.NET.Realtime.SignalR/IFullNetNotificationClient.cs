namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 管理端通知 Hub 的强类型客户端契约。
/// </summary>
public interface IFullNetNotificationClient
{
    Task ReceiveMessageAsync(RealtimeMessage message);
}
