using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>使用独立作用域续租和观察跨实例取消，避免与流请求共享数据库连接。</summary>
/// <param name="scopeFactory">独立数据库作用域工厂。</param>
/// <param name="clock">租约 UTC 时钟。</param>
/// <param name="logger">只记录续租失败，不记录聊天正文或密钥。</param>
internal sealed class AiChatGenerationLeaseMonitor(IServiceScopeFactory scopeFactory, IClock clock,
    ILogger<AiChatGenerationLeaseMonitor> logger)
{
    /// <summary>每次成功续租允许的最长静默时间。</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    /// <summary>持续观察生成所有权；数据库不可用、租约失效或远程取消都中止推理。</summary>
    /// <param name="scope">从请求可信上下文取得的所有者范围。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ownerUserId">已授权用户标识。</param>
    /// <param name="generationId">本代生成标识。</param>
    /// <param name="initialExpiresAtUtc">初始持久化租约到期时间。</param>
    /// <param name="requestBudget">需要中止的当前请求令牌源。</param>
    /// <param name="stopToken">执行结束时停止监视的独立令牌。</param>
    internal async Task WatchAsync(AiChatScope scope, Guid sessionId, Guid ownerUserId, Guid generationId,
        DateTimeOffset initialExpiresAtUtc, CancellationTokenSource requestBudget, CancellationToken stopToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        using var leaseDeadline = new CancellationTokenSource();
        using var deadlineRegistration = leaseDeadline.Token.Register(() => requestBudget.Cancel());
        var remaining = initialExpiresAtUtc - clock.UtcNow;
        leaseDeadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        try
        {
            do
            {
                var probeStarted = clock.UtcNow;
                // 即使驱动或租户解析忽略取消，独立期限仍会立即停止外部推理。
                using var round = CancellationTokenSource.CreateLinkedTokenSource(stopToken, leaseDeadline.Token);
                round.CancelAfter(TimeSpan.FromSeconds(10));
                var renewed = await RenewOnceAsync(scope, sessionId, ownerUserId, generationId, round.Token)
                    .WaitAsync(round.Token).ConfigureAwait(false);
                remaining = probeStarted + LeaseDuration - clock.UtcNow;
                if (!renewed || remaining <= TimeSpan.Zero || leaseDeadline.IsCancellationRequested)
                {
                    await requestBudget.CancelAsync().ConfigureAwait(false);
                    return;
                }
                leaseDeadline.CancelAfter(remaining);
            }
            while (await timer.WaitForNextTickAsync(stopToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI generation lease renewal failed for {GenerationId}.", generationId);
            await requestBudget.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>在新数据库作用域内验证并续租一代请求。</summary>
    /// <param name="scope">可信租户或 Host 范围。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ownerUserId">已授权用户标识。</param>
    /// <param name="generationId">本代生成标识。</param>
    /// <param name="cancellationToken">本轮续租取消令牌。</param>
    internal async Task<bool> RenewOnceAsync(AiChatScope scope, Guid sessionId, Guid ownerUserId,
        Guid generationId, CancellationToken cancellationToken)
    {
        await using var services = scopeFactory.CreateAsyncScope();
        var tenant = services.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        if (scope.TenantId is { } tenantId)
        {
            var resolver = services.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
            var active = await resolver.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (active is null) return false;
            tenant.SetTenant(active);
        }
        else tenant.SetHost();
        try
        {
            var now = clock.UtcNow;
            var commands = services.ServiceProvider.GetRequiredService<ICommandExecutor>();
            return await commands.ExecuteAsync(AiChatGenerationSql.Renew,
                AiChatSessionQueryService.BuildScopeParameters(scope, ownerUserId, ("SessionId", sessionId),
                    ("GenerationId", generationId), ("Now", now), ("ExpiresAtUtc", now + LeaseDuration)),
                cancellationToken).ConfigureAwait(false) == 1;
        }
        finally { tenant.Clear(); }
    }
}
