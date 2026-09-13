using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>每个收尾操作使用独立数据作用域；超时驱动不会占用后续清理的连接。</summary>
/// <param name="scopeFactory">独立数据作用域工厂。</param>
/// <param name="logger">只记录清理阶段失败，不记录原始异常中的敏感信息。</param>
internal sealed class AiChatCleanupScope(IServiceScopeFactory scopeFactory, ILogger<AiChatCleanupScope> logger)
{
    /// <summary>使用独立五秒期限执行清理，迟到写入仍由调用方 SQL 的生成代次约束保护。</summary>
    internal async Task<bool> RunAsync(TenantContext? trustedTenant, Func<IServiceProvider, CancellationToken, Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { return await RunCoreAsync(trustedTenant, action, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task<bool> RunCoreAsync(TenantContext? trustedTenant, Func<IServiceProvider, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var services = scopeFactory.CreateAsyncScope();
            var tenant = services.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            try
            {
                // 仅收敛已授权请求的本代状态；租户停用必须阻止新推理，但不能阻止既有清理。
                if (trustedTenant is not null) tenant.SetTenant(trustedTenant);
                else tenant.SetHost();
                await action(services.ServiceProvider, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally { tenant.Clear(); }
        }
        catch (Exception)
        {
            logger.LogWarning("AI generation cleanup failed.");
            return false;
        }
    }
}
