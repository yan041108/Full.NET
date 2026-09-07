using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>租户资源文件 pending/ready 孤儿对账循环；仅由 Worker 角色启动。</summary>
/// <param name="scopeFactory">作用域工厂。</param>
/// <param name="options">对账选项。</param>
/// <param name="databaseOptions">数据库提供程序。</param>
/// <param name="logger">日志。</param>
internal sealed class PendingTenantResourceFileReconciliationHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PendingTenantResourceFileReconciliationOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<PendingTenantResourceFileReconciliationHostedProcessor> logger)
    : BackgroundService
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PendingTenantResourceFileReconciliationLog.IterationFailed(logger, exception, _provider);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.PollSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>执行一轮对账；测试可直接调用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task<PendingTenantResourceFileReconciliationResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return PendingTenantResourceFileReconciliationResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<PendingTenantResourceFileReconciliationRunner>()
            .RunOnceAsync(currentOptions, cancellationToken)
            .ConfigureAwait(false);
        PendingTenantResourceFileReconciliationLog.IterationCompleted(
            logger,
            result.Promoted,
            result.Purged,
            result.Released,
            result.Skipped,
            result.BatchesExecuted,
            _provider);
        return result;
    }
}

/// <summary>租户资源文件对账日志。</summary>
internal static partial class PendingTenantResourceFileReconciliationLog
{
    /// <summary>记录本轮对账计数。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="promoted">提升条数。</param>
    /// <param name="purged">删除 pending 条数。</param>
    /// <param name="released">释放 ready 条数。</param>
    /// <param name="skipped">跳过条数。</param>
    /// <param name="batches">批次数。</param>
    /// <param name="provider">数据库提供程序。</param>
    [LoggerMessage(
        EventId = 4513,
        Level = LogLevel.Information,
        Message = "Files tenant resource reconciliation promoted {Promoted}, purged {Purged}, released {Released}, skipped {Skipped} in {Batches} batches for {Provider}")]
    public static partial void IterationCompleted(
        ILogger logger,
        int promoted,
        int purged,
        int released,
        int skipped,
        int batches,
        DatabaseProvider provider);

    /// <summary>记录本轮失败。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="exception">异常。</param>
    /// <param name="provider">数据库提供程序。</param>
    [LoggerMessage(
        EventId = 4514,
        Level = LogLevel.Error,
        Message = "Files tenant resource reconciliation iteration failed for {Provider}")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception,
        DatabaseProvider provider);
}
