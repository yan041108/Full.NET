namespace Full.NET.Realtime.SignalR;

/// <summary>
/// SignalR 实时通道配置；Hub 路径与 Redis Backplane 在此集中声明。
/// </summary>
public sealed class RealtimeOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Realtime";

    /// <summary>是否启用 SignalR Hub 与发布器；关闭时注入 <see cref="NullRealtimePublisher"/>。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>通知 Hub 映射路径，默认 <c>/hubs/notifications</c>。</summary>
    public string HubPath { get; set; } = "/hubs/notifications";

    /// <summary>
    /// Realtime Redis Backplane 专用连接串。Production/Staging 必须显式配置，禁止回退共享
    /// <c>ConnectionStrings:redis</c>；开发/测试仅在 <see cref="AllowSharedRedisInDevelopment"/> 为 true 时允许回退或与 Cache 共用。
    /// </summary>
    public string? RedisBackplaneConnectionString { get; set; }

    /// <summary>
    /// 仅 Development/Testing：允许 Realtime 与 Cache 共用同一 Redis 连接串或回退
    /// <c>ConnectionStrings:redis</c>。生产环境忽略该开关且必须物理隔离。
    /// </summary>
    public bool AllowSharedRedisInDevelopment { get; set; }

    /// <summary>传输模式；默认要求入口会话亲和，契约由 Helm/Ingress 落实。</summary>
    public RealtimeTransportMode TransportMode { get; set; } = RealtimeTransportMode.Default;

    /// <summary>
    /// 是否跳过 negotiate；仅与 <see cref="RealtimeTransportMode.WebSocketsOnly"/> 组合时允许关闭会话亲和。
    /// </summary>
    public bool SkipNegotiation { get; set; }

    /// <summary>
    /// 入口是否必须会话亲和。默认 true；仅 WebSocketsOnly + SkipNegotiation 时可设为 false。
    /// </summary>
    public bool RequireSessionAffinity { get; set; } = true;
}

/// <summary>Realtime 传输模式；应用只校验配置契约，亲和路由由部署层实现。</summary>
public enum RealtimeTransportMode
{
    /// <summary>默认协商传输；必须保持 Ingress 会话亲和。</summary>
    Default = 0,

    /// <summary>仅 WebSockets；与 SkipNegotiation 组合后才允许关闭亲和。</summary>
    WebSocketsOnly = 1,
}
