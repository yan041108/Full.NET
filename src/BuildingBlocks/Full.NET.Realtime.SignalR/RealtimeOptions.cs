namespace Full.NET.Realtime.SignalR;

/// <summary>
/// SignalR 实时通道配置；Hub 路径与 Redis Backplane 在此集中声明。
/// </summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>是否启用 SignalR Hub 与发布器；关闭时注入 <see cref="NullRealtimePublisher"/>。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>通知 Hub 映射路径，默认 <c>/hubs/notifications</c>。</summary>
    public string HubPath { get; set; } = "/hubs/notifications";

    /// <summary>
    /// 可选 Redis Backplane 连接串；为空时回退到 <c>ConnectionStrings:redis</c> 与 Cache 共用实例。
    /// </summary>
    public string? RedisBackplaneConnectionString { get; set; }
}
