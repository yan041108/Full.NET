using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion;

/// <summary>受治理缓存策略注册表。业务模块通过条目名获取策略与选项，禁止手写任意 TTL。</summary>
public interface ICachePolicyRegistry
{
    /// <summary>获取已注册策略；未知条目必须失败。</summary>
    CacheEntryPolicy GetRequired(string entryName);

    /// <summary>解析访问路径；C0/N0 分别返回 AuthorityRead/Bypass。</summary>
    CacheAccessDecision ResolveAccess(string entryName);

    /// <summary>
    /// 按策略生成 FusionCache 选项。C0/N0 必须抛错，避免调用方猜测绕过语义。
    /// </summary>
    FusionCacheEntryOptions CreateEntryOptions(string entryName);
}
