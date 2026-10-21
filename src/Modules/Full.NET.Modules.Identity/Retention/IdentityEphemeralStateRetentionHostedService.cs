using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

/// <summary>过期 Nonce 与 OAuth 临时状态在后台分批回收，不让登录请求承担历史清理。</summary>
internal sealed class IdentityEphemeralStateRetentionHostedService(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<IdentityEphemeralStateRetentionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                tenant.SetHost();
                try
                {
                    var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                    var mysql = databaseOptions.Value.Provider == DatabaseProvider.MySql;
                    foreach (var statement in new[] {
                        mysql ? SignatureNonceSql.DeleteExpiredMySql : SignatureNonceSql.DeleteExpiredSqlServer,
                        mysql ? OAuthAuthorizationStateSql.DeleteExpiredMySql : OAuthAuthorizationStateSql.DeleteExpired })
                    {
                        // 单语句及单轮均有预算；仅删除已失去安全作用的过期状态。
                        for (var batch = 0; batch < 10; batch++)
                            if (await commands.ExecuteAsync(statement,
                                IdentitySqlParameters.Create(("NowUtc", clock.UtcNow)), stoppingToken).ConfigureAwait(false) < 500)
                                break;
                    }
                }
                finally { tenant.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Identity ephemeral state cleanup failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
