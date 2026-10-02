using Microsoft.Extensions.Caching.Hybrid;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion;

/// <summary>受治理缓存策略注册表。业务模块通过条目名获取策略与选项，禁止手写任意 TTL。</summary>
public interface ICachePolicyRegistry
{
    /// <summary>获取已注册策略；未知条目必须失败。</summary>
    /// <returns>已注册的缓存策略；entryName 未注册时抛出异常，避免静默降级。</returns>
    CacheEntryPolicy GetRequired(string entryName);

    /// <summary>返回当前进程已登记的全部缓存策略目录，供管理控制面只读展示。</summary>
    /// <returns>当前进程已登记的全部缓存策略只读列表；无策略时为空列表，不为 null。</returns>
    IReadOnlyList<CacheEntryPolicy> ListPolicies();

    /// <summary>解析访问路径；C0/N0 分别返回 AuthorityRead/Bypass。</summary>
    /// <returns>该条目对应的访问决策；C0/N0 分别返回 AuthorityRead/Bypass。</returns>
    CacheAccessDecision ResolveAccess(string entryName);

    /// <summary>
    /// 按策略生成 FusionCache 选项。C0/N0 必须抛错，避免调用方猜测绕过语义。
    /// </summary>
    /// <returns>按策略生成的 FusionCache 选项；C0/N0 条目抛出异常。</returns>
    FusionCacheEntryOptions CreateEntryOptions(string entryName);

    /// <summary>
    /// 按策略生成 HybridCache 选项，并显式区分正常与负缓存寿命。C0/N0 必须抛错。
    /// </summary>
    /// <returns>按策略生成的 HybridCache 选项，区分正常与负缓存寿命；C0/N0 条目抛出异常。</returns>
    HybridCacheEntryOptions CreateHybridEntryOptions(
        string entryName,
        CacheEntryLifetime lifetime = CacheEntryLifetime.Normal);
}
