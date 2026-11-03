using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Payments.Features.SubscriptionBilling;

/// <summary>只接续已验证的资金事实，不自动重新下单或发起扣款。</summary>
internal sealed partial class SubscriptionFulfillmentWorker(IServiceScopeFactory scopes, ILogger<SubscriptionFulfillmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var tenant = sp.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(30));
                var sql = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider == DatabaseProvider.MySql
                    ? SubscriptionFulfillmentSql.DueMySql : SubscriptionFulfillmentSql.DueSqlServer;
                var ids = await sp.GetRequiredService<IQueryExecutor>().QueryAsync<Guid>(sql,
                    PaymentSqlParameters.Create(("Now", sp.GetRequiredService<IClock>().UtcNow)), budget.Token).ConfigureAwait(false);
                var processor = sp.GetRequiredService<SubscriptionFulfillmentProcessor>();
                foreach (var id in ids)
                {
                    try { await processor.ProcessAsync(id, budget.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { RecoveryFailed(logger, e); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception e) { RecoveryFailed(logger, e); }
            finally { tenant.Clear(); }
        }
    }
    [LoggerMessage(EventId = 7491, Level = LogLevel.Warning, Message = "订阅履约未完成；已保存的资金关联将继续恢复。")]
    private static partial void RecoveryFailed(ILogger logger, Exception exception);
}
