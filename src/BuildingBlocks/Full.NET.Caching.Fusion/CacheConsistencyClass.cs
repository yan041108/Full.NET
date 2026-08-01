namespace Full.NET.Caching.Fusion;

/// <summary>
/// 缓存一致性类别。枚举名面向代码，配置侧同时接受 C0/S0-L2/S1/S2/N0 别名。
/// </summary>
public enum CacheConsistencyClass
{
    /// <summary>C0：权威强一致决策，不得依赖缓存正确性。</summary>
    AuthorityCritical = 0,

    /// <summary>S0-L2：仅共享 L2，禁用节点 L1。</summary>
    SharedL2Only = 1,

    /// <summary>S1：重要业务投影，L1+L2+Backplane，提交后直接失效。</summary>
    ImportantBusiness = 2,

    /// <summary>S2：可降级展示，仅显式配置才允许 Fail-Safe。</summary>
    DegradableDisplay = 3,

    /// <summary>N0：不缓存，直接读权威源。</summary>
    NotCached = 4,
}
