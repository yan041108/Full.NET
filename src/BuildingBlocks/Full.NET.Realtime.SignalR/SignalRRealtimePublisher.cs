using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 通过 SignalR 组播实现 <see cref="IRealtimePublisher"/>，隔离 Hub 上下文。
/// </summary>
/// <remarks>
/// <para>本类只依赖 <see cref="IHubContext{THub}"/>，禁止业务模块直接引用 Hub 类型或连接 API；
/// 多实例部署时通过 Redis Backplane 跨实例分发，发送结果只反映 SignalR 服务端投递状态，
/// 不代表客户端已接收或处理。</para>
/// <para>调用方传入的 <see cref="CancellationToken"/> 会透传到 SignalR 发送边界；
/// 超时与底层异常按原异常抛出，是否重试由调用方决定，本实现不进行任何重试或补偿。</para>
/// </remarks>
internal sealed class SignalRRealtimePublisher(
    IHubContext<FullNetNotificationHub> hubContext,
    ICurrentTenant currentTenant)
    : IRealtimePublisher
{
    /// <summary>
    /// 向指定用户对应的私有组推送消息，组名由 <see cref="RealtimeGroups.User"/> 规范化得到。
    /// </summary>
    /// <param name="userId">目标用户标识。</param>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    public Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            "user",
            RealtimeGroups.User(userId),
            message,
            cancellationToken);

    /// <summary>
    /// 向当前租户对应的广播组推送消息。
    /// </summary>
    /// <param name="tenantId">目标租户标识，必须与当前租户上下文一致。</param>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    public Task PublishToTenantAsync(
        Guid tenantId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable
            || currentTenant.IsHost
            || currentTenant.Id != tenantId)
        {
            throw new InvalidOperationException(
                "Realtime tenant target must match the active tenant context.");
        }

        return PublishAsync(
            "tenant",
            RealtimeGroups.Tenant(tenantId),
            message,
            cancellationToken);
    }

    /// <summary>
    /// 向 Host 广播组推送消息，仅允许明确的 Host 上下文调用。
    /// </summary>
    /// <param name="message">包含稳定机器码与可选结构化数据的消息。</param>
    /// <param name="cancellationToken">用于取消 SignalR 发送任务的令牌。</param>
    public Task PublishToHostBroadcastAsync(
        RealtimeMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsHost)
        {
            throw new InvalidOperationException(
                "Realtime host broadcast requires an active host context.");
        }

        return PublishAsync(
            "host",
            RealtimeGroups.HostBroadcast,
            message,
            cancellationToken);
    }

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
