using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>只由 Worker 接续持久席位意图；每批有时间预算，每项有数据库租约，多个实例可安全竞争。</summary>
internal sealed partial class TenantSeatRecoveryWorker(IServiceScopeFactory scopes, ILogger<TenantSeatRecoveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                tenant.SetHost();
                try
                {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    budget.CancelAfter(TimeSpan.FromSeconds(25));
                    var database = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                    var statement = database.Provider == DatabaseProvider.SqlServer
                        ? TenantSeatOperationSql.DueSqlServer : TenantSeatOperationSql.DueMySql;
                    var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
                    var ids = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QueryAsync<Guid>(statement,
                        IdentitySqlParameters.Create(("Now", now)), budget.Token).ConfigureAwait(false);
                    var processor = scope.ServiceProvider.GetRequiredService<TenantSeatOperationProcessor>();
                    foreach (var id in ids)
                    {
                        try { await processor.ProcessAsync(id, budget.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception exception) { RecoveryFailed(logger, exception); }
                    }
                }
                finally { tenant.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception exception) { RecoveryFailed(logger, exception); }
        }
    }

    [LoggerMessage(EventId = 7481, Level = LogLevel.Warning, Message = "成员席位恢复批次未完成，持久工作项将继续重试。")]
    private static partial void RecoveryFailed(ILogger logger, Exception exception);
}
