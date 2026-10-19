namespace Full.NET.Caching.Fusion;

/// <summary><c>Cache:Entries</c> 下单个条目的配置投影。</summary>
public sealed class CacheEntryDefinitionOptions
{
    private TimeSpan? _jitter;
    /// <summary>所属模块键。</summary>
    public string OwnerModule { get; set; } = string.Empty;

    /// <summary>一致性类别，接受 C0/S0-L2/S1/S2/N0 或枚举名。</summary>
    public string ConsistencyClass { get; set; } = string.Empty;

    /// <summary>L1 时长。</summary>
    public TimeSpan L1Duration { get; set; }

    /// <summary>L2 时长。</summary>
    public TimeSpan L2Duration { get; set; }

    /// <summary>TTL 抖动；未赋值时继承全局设置，显式零表示关闭。保留 TimeSpan 公共属性类型兼容。</summary>
    public TimeSpan Jitter
    {
        get => _jitter ?? TimeSpan.Zero;
        set => _jitter = value;
    }

    /// <summary>区分未配置与显式零，避免配置绑定后误用全局抖动覆盖零值。</summary>
    internal bool HasExplicitJitter => _jitter.HasValue;

    /// <summary>负缓存时长。</summary>
    public TimeSpan? NegativeDuration { get; set; }

    /// <summary>是否启用 Fail-Safe；仅 S2 允许为 true。</summary>
    public bool FailSafeEnabled { get; set; }

    /// <summary>Fail-Safe 从条目写入起的保留上限；启用时必须显式配置。</summary>
    public TimeSpan? FailSafeMaxDuration { get; set; }

    /// <summary>是否要求版本复核。</summary>
    public bool RequiresVersionRecheck { get; set; }

    /// <summary>拥有者执行的业务 JSON 字节预算，范围 1 字节至 64 MiB；不是 CLR 类型共享预算或进程内存硬上限。</summary>
    public int MaxSerializedBytes { get; set; } = 65_536;
}
