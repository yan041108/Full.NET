namespace Full.NET.Caching.Fusion;

/// <summary>
/// 缓存条目生命周期分类；区分正常命中值与负缓存（不存在/已删除）两种存储模式。
/// </summary>
public enum CacheEntryLifetime
{
    /// <summary>
    /// 正常生命周期：对应真实存在且可返回的业务数据，使用 L1/L2 正常 TTL。
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 负缓存生命周期：对应权威源确认不存在或已删除的标识，使用独立 NegativeDuration，
    /// 避免短时间内重复穿透到数据库。
    /// </summary>
    Negative = 1,
}
