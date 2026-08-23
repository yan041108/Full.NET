using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR.Health;

/// <summary>
/// Realtime Redis Backplane 连通性探测实现；用于 ready 健康检查独立判定 Backplane 是否可达。
/// </summary>
/// <remarks>
/// <para>该探针不复用 SignalR 运行连接，每次都建立短连接以避免后台重连语义掩盖当前 Backplane 故障；
/// 探针失败时 ready 健康检查会标记实例未就绪，触发编排器停止路由流量。</para>
/// <para>连接超时与异步超时均设为 1 秒，确保 ready 探针在编排器超时窗口内返回；
/// 失败立即抛出原异常，由调用方决定是否记录与告警。</para>
/// </remarks>
internal sealed class RealtimeBackplaneProbe : IRealtimeBackplaneProbe
{
    /// <summary>
    /// 对指定 Redis 连接串执行一次 PING 探测，验证 Backplane 可达性。
    /// </summary>
    /// <param name="connectionString">目标 Redis 连接串，由调用方提供并保证非空。</param>
    /// <param name="cancellationToken">用于取消探测的令牌；用于 ready 探针时通常带短超时。</param>
    /// <returns>表示探测完成的任务；任务失败即代表 Backplane 不可达。</returns>
    /// <remarks>
    /// 探针以 <c>AbortOnConnectFail=true</c>、零重试与 1 秒超时建立连接，确保 Backplane 故障快速暴露；
    /// 不会缓存或复用连接，避免与 SignalR 运行连接的恢复语义相互掩盖。
    /// </remarks>
    public async Task PingAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        // ready 探针必须快速失败，不能沿用运行连接的后台重连语义拖住实例摘流。
        configuration.AbortOnConnectFail = true;
        configuration.ConnectRetry = 0;
        configuration.ConnectTimeout = 1000;
        configuration.AsyncTimeout = 1000;
        await using var connection = await ConnectionMultiplexer
            .ConnectAsync(configuration)
            .WaitAsync(cancellationToken);
        _ = await connection
            .GetDatabase()
            .PingAsync()
            .WaitAsync(cancellationToken);
    }
}
