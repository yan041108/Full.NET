namespace Full.NET.Caching.Fusion;

/// <summary>
/// Full.NET 缓存全局配置，承载 L1/L2 分层缓存、Backplane 同步与序列化的默认治理参数。
/// 业务模块不得在代码中硬编码任意 TTL，必须通过 <see cref="Entries"/> 按条目名注册受治理策略。
/// </summary>
public sealed class CacheOptions
{
    /// <summary>每个节点的 L1 业务载荷权重预算，默认 10000；治理条目权重为 1，SDK 标签元数据不计入，不能解释为字节或进程内存上限。</summary>
    public int L1PayloadCapacity { get; set; } = 10_000;

    internal const int MaximumDeserializationBytes = 64 * 1024 * 1024;

    /// <summary>全局 L2 序列化输入的解析前字节上限，默认 1 MiB，允许 1 字节至 64 MiB；独立于条目业务载荷预算，不限制 Redis SDK 已分配的缓冲区。</summary>
    public int MaxDeserializationBytes { get; set; } = 1024 * 1024;

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

    /// <summary>同一应用所有 API/Worker 共用的命名空间；不得使用节点标识。</summary>
    public string ApplicationName { get; set; } = "fullnet";

    /// <summary>提交后清理总预算，独立于请求取消，默认 5 秒。</summary>
    public TimeSpan InvalidationTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>单批原始失效目标数量上限，包含重复输入，默认 1000。</summary>
    public int InvalidationMaxBatchItems { get; set; } = 1000;

    /// <summary>单批原始条目名和目标的 UTF-8 总字节上限，默认 256 KiB。</summary>
    public int InvalidationMaxBatchBytes { get; set; } = 256 * 1024;

    /// <summary>当前进程所有批次共同占用的 SDK 失效操作上限，默认 4。</summary>
    public int InvalidationMaxConcurrency { get; set; } = 4;

    /// <summary>当前进程允许同时存在的执行/等待批次数，默认 32；到顶立即拒绝，避免无界排队。</summary>
    public int InvalidationMaxConcurrentBatches { get; set; } = 32;

    /// <summary>诊断快照最大年龄；后台轮询停止时也必须撤销临时放宽，默认 1 分钟。</summary>
    public TimeSpan DiagnosticSnapshotMaxAge { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Redis 单次连接与命令超时预算，默认 1 秒。</summary>
    public TimeSpan RedisOperationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>SDK 恢复队列的淘汰阈值；并发可超过该值，不是严格条目数或内存上限，也不承诺可靠投递。</summary>
    public int AutoRecoveryMaxItems { get; set; } = 1000;

    /// <summary>单项故障恢复最多尝试次数，达到上限后依赖业务兜底。</summary>
    public int AutoRecoveryMaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Redis L2 共享缓存与 Backplane 通信连接字符串。
    /// 生产/预发环境必须与 Realtime Backplane 使用不同的 Redis 实例，避免 Pub/Sub 风暴扩散到缓存层。
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 按条目名注册的缓存策略。默认已内置 <c>tenancy.tenant-resolution</c>=C0；
    /// 此处同名配置可覆盖，但不得为全部业务强塞同一 TTL。
    /// </summary>
    public Dictionary<string, CacheEntryDefinitionOptions> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
