using Microsoft.AspNetCore.SignalR;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 通过 SignalR 组播实现 <see cref="IRealtimePublisher"/>，隔离 Hub 上下文。
/// </summary>
internal sealed class SignalRRealtimePublisher(
    IHubContext<FullNetNotificationHub, IFullNetNotificationClient> hubContext)
    : IRealtimePublisher
{
    public Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(RealtimeGroups.User(userId))
            .ReceiveMessageAsync(message);

    public Task PublishToGroupAsync(
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(groupName)
            .ReceiveMessageAsync(message);
}
