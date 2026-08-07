namespace Full.NET.Caching.Fusion;

/// <summary>稳定缓存条目名。业务模块应引用常量，避免魔法字符串漂移。</summary>
public static class CacheEntryNames
{
    /// <summary>租户解析（按 Id/域名）共享条目，默认归类为 S1。</summary>
    public const string TenantResolution = "tenancy.tenant-resolution";

    /// <summary>Host 限时诊断策略快照，默认归类为 S1 短 TTL。</summary>
    public const string DiagnosticPolicy = "settings.diagnostic-policy";

    /// <summary>当前用户 Grid 展示偏好，默认归类为 S2。</summary>
    public const string GridPreference = "settings.grid-preference";
}
