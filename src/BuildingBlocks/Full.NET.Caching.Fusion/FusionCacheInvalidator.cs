using Full.NET.Caching.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion;

/// <summary>
/// 将 Full.NET 稳定失效语义映射到 FusionCache。所有 Options 都留在 Provider 内部，
/// 避免业务模块自行组合后台执行、异常传播或 Backplane 开关。
/// </summary>
internal sealed class FusionCacheInvalidator(
    IFusionCache cache,
    ICachePolicyRegistry policies) : ICacheInvalidator
{
    /// <inheritdoc />
    public async ValueTask RemoveAsync(
        string entryName,
        string key,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await cache.RemoveAsync(
                key,
                CreateOptions(entryName, scope),
                token: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveByTagAsync(
        string entryName,
        string tag,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        await cache.RemoveByTagAsync(
                tag,
                CreateOptions(entryName, scope),
                token: cancellationToken)
            .ConfigureAwait(false);
    }

    private FusionCacheEntryOptions CreateOptions(
        string entryName,
        CacheInvalidationScope scope)
    {
        var options = policies.CreateEntryOptions(entryName);
        switch (scope)
        {
            case CacheInvalidationScope.CurrentNodeOnly:
                options.SetSkipDistributedCache(
                    skip: true,
                    skipBackplaneNotifications: true);
                break;
            case CacheInvalidationScope.AllLayersSynchronous:
                // 提交后失效必须等到 L2 删除与 Backplane 发布完成，失败由上层按业务语义处理。
                options.AllowBackgroundDistributedCacheOperations = false;
                options.ReThrowDistributedCacheExceptions = true;
                options.AllowBackgroundBackplaneOperations = false;
                options.ReThrowBackplaneExceptions = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "未知缓存失效范围。");
        }

        return options;
    }
}
