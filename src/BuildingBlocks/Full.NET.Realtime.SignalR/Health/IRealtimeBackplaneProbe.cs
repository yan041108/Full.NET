namespace Full.NET.Realtime.SignalR.Health;

/// <summary>
/// 对实时 Backplane 执行最小可达性探测，供健康检查确认广播基础设施是否具备对外服务能力。
/// </summary>
internal interface IRealtimeBackplaneProbe
{
    /// <summary>
    /// 使用给定连接串执行一次无副作用探测。
    /// </summary>
    /// <param name="connectionString">Backplane 的权威连接串；调用方必须确保其来自受信配置源而非请求输入。</param>
    /// <param name="cancellationToken">用于约束探测时长的取消令牌，避免健康检查长时间阻塞。</param>
    Task PingAsync(
        string connectionString,
        CancellationToken cancellationToken);
}
