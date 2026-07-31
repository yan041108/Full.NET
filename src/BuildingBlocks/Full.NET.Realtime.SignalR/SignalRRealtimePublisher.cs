using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 通过 SignalR 组播实现 <see cref="IRealtimePublisher"/>，隔离 Hub 上下文。
/// </summary>
internal sealed class SignalRRealtimePublisher(
    IHubContext<FullNetNotificationHub> hubContext)
    : IRealtimePublisher
{
    public Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            "user",
            RealtimeGroups.User(userId),
            message,
            cancellationToken);

    public Task PublishToGroupAsync(
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            "group",
            groupName,
            message,
            cancellationToken);

    private async Task PublishAsync(
        string target,
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            // 未类型化代理保留同一客户端方法与载荷，同时把调用方取消传入 SignalR 发送边界。
            await hubContext.Clients
                .Group(groupName)
                .SendCoreAsync(
                    nameof(IFullNetNotificationClient.ReceiveMessageAsync),
                    [message],
                    cancellationToken)
                .ConfigureAwait(false);
            RealtimePublishTelemetry.Record(
                startedTimestamp,
                target,
                "success");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RealtimePublishTelemetry.Record(
                startedTimestamp,
                target,
                "canceled");
            throw;
        }
        catch (TimeoutException)
        {
            // SignalR/Redis 原生超时属于独立容量故障分类，原异常仍由调用方决定是否重试。
            RealtimePublishTelemetry.Record(
                startedTimestamp,
                target,
                "timeout");
            throw;
        }
        catch (Exception)
        {
            RealtimePublishTelemetry.Record(
                startedTimestamp,
                target,
                "failure");
            throw;
        }
    }
}
