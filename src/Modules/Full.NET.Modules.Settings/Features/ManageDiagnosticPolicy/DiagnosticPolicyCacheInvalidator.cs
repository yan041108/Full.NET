using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>
/// 诊断策略缓存提交后失效：先清本机 L1，再尽力清 L2/Backplane；失败只告警，不写 Outbox。
/// </summary>
internal sealed class DiagnosticPolicyCacheInvalidator(
    IFusionCache cache,
    IHostEnvironment environment,
    ICachePolicyRegistry policies,
    ILogger<DiagnosticPolicyCacheInvalidator> logger)
{
    public static string BuildCacheKey(string environmentName) =>
        CacheKeyBuilder.ForGlobal(
            environmentName,
            "settings",
            "diagnostic-policy",
            "current",
            "v1");

    public async Task InvalidateAfterCommitAsync(CancellationToken cancellationToken)
    {
        _ = policies.GetRequired(CacheEntryNames.DiagnosticPolicy);
        var key = BuildCacheKey(environment.EnvironmentName);
        try
        {
            var local = new FusionCacheEntryOptions().SetSkipDistributedCache(
                skip: true,
                skipBackplaneNotifications: true);
            await cache.RemoveAsync(key, local, token: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "诊断策略缓存本地 L1 失效失败；依赖 TTL/权威源收敛。");
            return;
        }

        try
        {
            var distributed = new FusionCacheEntryOptions
            {
                AllowBackgroundDistributedCacheOperations = false,
                ReThrowDistributedCacheExceptions = true,
                AllowBackgroundBackplaneOperations = false,
                ReThrowBackplaneExceptions = true,
            };
            await cache.RemoveAsync(key, distributed, token: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "诊断策略缓存 L2/Backplane 失效失败；本节点 L1 已清理。");
        }
    }
}
