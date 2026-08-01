namespace Full.NET.Caching.Fusion;

/// <summary>
/// 受治理的缓存条目策略。业务模块不得自行拼装任意 FusionCacheEntryOptions。
/// </summary>
/// <param name="EntryName">稳定条目名，例如 <c>tenancy.tenant-resolution</c>。</param>
/// <param name="OwnerModule">所属模块键，用于低基数指标。</param>
/// <param name="ConsistencyClass">一致性类别。</param>
/// <param name="L1Duration">节点内存缓存时长；S0-L2 忽略。</param>
/// <param name="L2Duration">共享分布式缓存时长。</param>
/// <param name="Jitter">TTL 抖动上限，降低同步过期尖峰。</param>
/// <param name="NegativeDuration">负缓存时长；null 表示不使用负缓存。</param>
/// <param name="FailSafeEnabled">是否启用 Fail-Safe；仅 S2 可显式开启。</param>
/// <param name="RequiresVersionRecheck">命中后是否必须复核版本/权威源。</param>
/// <param name="MaxSerializedBytes">序列化体积上限，超限应计量并拒绝回填。</param>
public sealed record CacheEntryPolicy(
    string EntryName,
    string OwnerModule,
    CacheConsistencyClass ConsistencyClass,
    TimeSpan L1Duration,
    TimeSpan L2Duration,
    TimeSpan Jitter,
    TimeSpan? NegativeDuration,
    bool FailSafeEnabled,
    bool RequiresVersionRecheck,
    int MaxSerializedBytes)
{
    /// <summary>
    /// S1/S0-L2 必须在事务提交后直接删除 L1/L2（并广播 Backplane），禁止改走 Outbox。
    /// </summary>
    public bool RequiresDirectInvalidation =>
        ConsistencyClass is CacheConsistencyClass.ImportantBusiness
            or CacheConsistencyClass.SharedL2Only;

    /// <summary>导出低基数指标使用的一致性类别标签。</summary>
    public string ConsistencyClassTag => ConsistencyClass switch
    {
        CacheConsistencyClass.AuthorityCritical => "c0",
        CacheConsistencyClass.SharedL2Only => "s0_l2",
        CacheConsistencyClass.ImportantBusiness => "s1",
        CacheConsistencyClass.DegradableDisplay => "s2",
        CacheConsistencyClass.NotCached => "n0",
        _ => "unknown",
    };
}
