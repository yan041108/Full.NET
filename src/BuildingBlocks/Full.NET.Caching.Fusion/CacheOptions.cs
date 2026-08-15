namespace Full.NET.Caching.Fusion;

/// <summary>
/// Full.NET 缓存全局配置，承载 L1/L2 分层缓存、Backplane 同步与序列化的默认治理参数。
/// 业务模块不得在代码中硬编码任意 TTL，必须通过 <see cref="Entries"/> 按条目名注册受治理策略。
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// 配置节名称，默认绑定到 <c>Cache</c> 根节点。
    /// </summary>
    public const string SectionName = "Cache";

    /// <summary>
    /// 未显式声明条目的兜底 L2 缓存时长；受治理条目必须忽略本值，禁止用作统一 TTL。
    /// </summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 全局默认 TTL 抖动上限，用于降低多实例在同一时刻缓存同步过期导致的回源尖峰。
    /// </summary>
    public TimeSpan Jitter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Redis L2 共享缓存与 Backplane 通信连接字符串。
    /// 生产/预发环境必须与 Realtime Backplane 使用不同的 Redis 实例，避免 Pub/Sub 风暴扩散到缓存层。
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 按条目名注册的缓存策略。默认已内置 <c>tenancy.tenant-resolution</c>=S1；
    /// 此处同名配置可覆盖，但不得为全部业务强塞同一 TTL。
    /// </summary>
    public Dictionary<string, CacheEntryDefinitionOptions> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
