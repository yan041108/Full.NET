namespace Full.NET.Caching.Fusion;

/// <summary>稳定缓存条目名。业务模块应引用常量，避免魔法字符串漂移。</summary>
public static class CacheEntryNames
{
    /// <summary>租户解析（按 Id/域名）的历史缓存治理条目，默认 C0；准入读取权威源。</summary>
    public const string TenantResolution = "tenancy.tenant-resolution";

    /// <summary>Host 诊断策略历史缓存条目，目录保留 S1 元数据；运行快照由权威轮询维护。</summary>
    public const string DiagnosticPolicy = "settings.diagnostic-policy";

    /// <summary>当前用户 Grid 展示偏好，默认归类为 S2。</summary>
    public const string GridPreference = "settings.grid-preference";
}
