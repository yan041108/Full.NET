namespace Full.NET.Caching.Fusion;

/// <summary>稳定缓存条目名。业务模块应引用常量，避免魔法字符串漂移。</summary>
public static class CacheEntryNames
{
    /// <summary>租户解析（按 Id/域名）共享条目，默认归类为 S1。</summary>
    public const string TenantResolution = "tenancy.tenant-resolution";
}
