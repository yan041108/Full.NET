using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion.Health;

/// <summary>用独立的空键失效维持实际通信证据；不写业务数据、不创建 Redis 键，也不改变只读健康检查。</summary>
internal sealed class CacheBackplaneHeartbeat(
    IFusionCache cache, IOptions<CacheOptions> options, ILogger<CacheBackplaneHeartbeat> logger) : BackgroundService
{
    // 每个进程固定一个不可与业务键碰撞的目标，避免故障恢复队列随轮询增长。
    private readonly string _key = $"fullnet:health:backplane:{Guid.NewGuid():N}";
    private readonly FusionCacheEntryOptions _entryOptions = new()
    {
        SkipDistributedCacheRead = true,
        SkipDistributedCacheWrite = true,
        SkipBackplaneNotifications = false,
        AllowBackgroundBackplaneOperations = false,
        AllowBackgroundDistributedCacheOperations = false,
        ReThrowBackplaneExceptions = true,
    };

    internal async ValueTask PulseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Value.RedisOperationTimeout);
        // 仍经过注册的 SDK 与观察器；不能由定时器直接把状态设为 Operational。
        await cache.RemoveAsync(_key, _entryOptions, deadline.Token).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PulseAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // 不记录 Redis 异常文本中的端点/凭据；失败仍由实际背板观察器交给健康检查处理。
                logger.LogWarning("缓存背板通信探测未完成，保留实际健康状态并在下一周期重试。");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
