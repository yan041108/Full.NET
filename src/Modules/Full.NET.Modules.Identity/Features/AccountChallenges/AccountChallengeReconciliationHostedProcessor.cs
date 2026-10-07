using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

/// <summary>Worker 专属巡检；每轮一页，失败保持游标以便幂等重试，多实例通过行 CAS 安全收敛。</summary>
internal sealed partial class AccountChallengeReconciliationHostedProcessor(
    IServiceScopeFactory scopes, IOptionsMonitor<AccountChallengeReconciliationOptions> options,
    ILogger<AccountChallengeReconciliationHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? afterId = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            // 无效热更新不能产生紧循环；未能读取有效配置时使用固定安全间隔。
            var pollSeconds = 60;
            try
            {
                var current = options.CurrentValue;
                pollSeconds = current.PollSeconds;
                if (current.Enabled)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                    tenant.SetHost();
                    try
                    {
                        var result = await scope.ServiceProvider.GetRequiredService<AccountChallengeReconciliationRunner>()
                            .RunPageAsync(afterId, current.BatchSize, stoppingToken).ConfigureAwait(false);
                        afterId = result.NextAfterId;
                        LogPage(logger, result.Scanned, result.Reconciled);
                    }
                    finally { tenant.Clear(); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { LogFailure(logger); }
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 4533, Level = LogLevel.Information, Message = "Challenge delivery reconciliation scanned {Scanned}, reconciled {Reconciled}.")]
    private static partial void LogPage(ILogger logger, int scanned, int reconciled);
    // 数据库错误可携带敏感参数，只记录固定诊断，不附加异常或地址、摘要、凭据。
    [LoggerMessage(EventId = 4534, Level = LogLevel.Warning, Message = "Challenge delivery reconciliation page failed.")]
    private static partial void LogFailure(ILogger logger);
}

internal static class AccountChallengeReconciliationServiceCollectionExtensions
{
    internal static IServiceCollection AddAccountChallengeReconciliation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccountChallengeReconciliationOptions>().Bind(configuration.GetSection(AccountChallengeReconciliationOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AccountChallengeReconciliationOptions>, AccountChallengeReconciliationOptionsValidator>());
        services.TryAddScoped<AccountChallengeReconciliationRunner>();
        services.AddHostedService<AccountChallengeReconciliationHostedProcessor>();
        return services;
    }
}
