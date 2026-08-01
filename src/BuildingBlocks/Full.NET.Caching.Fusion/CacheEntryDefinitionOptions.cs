namespace Full.NET.Caching.Fusion;

/// <summary><c>Cache:Entries</c> 下单个条目的配置投影。</summary>
public sealed class CacheEntryDefinitionOptions
{
    /// <summary>所属模块键。</summary>
    public string OwnerModule { get; set; } = string.Empty;

    /// <summary>一致性类别，接受 C0/S0-L2/S1/S2/N0 或枚举名。</summary>
    public string ConsistencyClass { get; set; } = string.Empty;

    /// <summary>L1 时长。</summary>
    public TimeSpan L1Duration { get; set; }

    /// <summary>L2 时长。</summary>
    public TimeSpan L2Duration { get; set; }

    /// <summary>TTL 抖动。</summary>
    public TimeSpan Jitter { get; set; }

    /// <summary>负缓存时长。</summary>
    public TimeSpan? NegativeDuration { get; set; }

    /// <summary>是否启用 Fail-Safe；仅 S2 允许为 true。</summary>
    public bool FailSafeEnabled { get; set; }

    /// <summary>是否要求版本复核。</summary>
    public bool RequiresVersionRecheck { get; set; }

    /// <summary>最大序列化字节数。</summary>
    public int MaxSerializedBytes { get; set; } = 65_536;
}
