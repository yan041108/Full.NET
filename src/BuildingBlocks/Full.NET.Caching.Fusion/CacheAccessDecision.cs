namespace Full.NET.Caching.Fusion;

/// <summary>解析缓存条目后应采取的访问路径，避免把 C0/N0 误当成可写缓存策略。</summary>
public enum CacheAccessKind
{
    /// <summary>可按策略读写 FusionCache。</summary>
    UseCache = 0,

    /// <summary>C0：必须以权威源完成决策。</summary>
    AuthorityRead = 1,

    /// <summary>N0：绕过缓存。</summary>
    Bypass = 2,
}

/// <summary>缓存访问决策。</summary>
/// <param name="Kind">访问路径。</param>
/// <param name="ConsistencyClass">条目一致性类别。</param>
/// <param name="EntryName">稳定条目名。</param>
public readonly record struct CacheAccessDecision(
    CacheAccessKind Kind,
    CacheConsistencyClass ConsistencyClass,
    string EntryName);
