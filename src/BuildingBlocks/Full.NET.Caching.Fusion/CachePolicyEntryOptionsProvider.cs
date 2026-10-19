using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion;

/// <summary>为规范键的 Hybrid 适配提供未在 Hybrid Options 暴露的策略；不创建第二套缓存访问抽象。</summary>
internal sealed class CachePolicyEntryOptionsProvider(ICachePolicyRegistry policies) : FusionCacheEntryOptionsProvider
{
    private readonly IReadOnlyDictionary<string, CacheEntryPolicy> _policies = policies.ListPolicies()
        .ToDictionary(policy => policy.EntryName, StringComparer.OrdinalIgnoreCase);

    public override FusionCacheEntryOptions? GetEntryOptions(
        FusionCacheEntryOptionsProviderContext ctx, string key, out bool canMutate)
    {
        canMutate = true;
        // 只识别统一构造器的稳定模块/资源段，复合 ID 中的冒号不参与策略匹配。
        var segments = key.Split(':', 7);
        if (segments.Length != 7 || segments[0] != "fullnet"
            || !_policies.TryGetValue($"{segments[3]}.{segments[4]}", out var policy)
            || !string.Equals(policy.OwnerModule, segments[3], StringComparison.OrdinalIgnoreCase))
            return null;

        var options = ctx.DuplicateDefaultEntryOptions();
        options.JitterMaxDuration = policy.Jitter;
        options.IsFailSafeEnabled = policy.FailSafeEnabled;
        if (policy.FailSafeMaxDuration is { } retention) options.FailSafeMaxDuration = retention;
        // L1/L2 读写标志仍由显式选项决定，删除不得继承 S0 的 SkipMemoryCache。
        return options;
    }
}
