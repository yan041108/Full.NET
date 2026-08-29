namespace Full.NET.Caching.Abstractions;

/// <summary>
/// 受治理的缓存失效边界。业务模块提供稳定条目名与已隔离的键或标签，Provider 负责映射具体缓存选项。
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>按指定传播范围删除一个缓存键。</summary>
    /// <param name="entryName">已注册的稳定缓存条目名。</param>
    /// <param name="key">包含环境与租户或 Host 范围的完整缓存键。</param>
    /// <param name="scope">本机或全层同步传播范围。</param>
    /// <param name="cancellationToken">取消缓存失效等待的令牌。</param>
    ValueTask RemoveAsync(
        string entryName,
        string key,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>按指定传播范围删除一个缓存标签下的条目。</summary>
    /// <param name="entryName">已注册的稳定缓存条目名。</param>
    /// <param name="tag">由统一键策略产生的低基数失效标签。</param>
    /// <param name="scope">本机或全层同步传播范围。</param>
    /// <param name="cancellationToken">取消缓存失效等待的令牌。</param>
    ValueTask RemoveByTagAsync(
        string entryName,
        string tag,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken = default);
}
