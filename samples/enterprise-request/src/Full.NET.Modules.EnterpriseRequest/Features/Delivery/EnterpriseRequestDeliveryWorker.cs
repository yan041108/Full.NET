using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>应用交付 Worker 配置，停止后台运行不改变文件读取的授权规则。</summary>
internal sealed class EnterpriseRequestDeliveryOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 5;
}

/// <summary>扫描游标按宿主实例隔离，不让停用租户占满每轮最早的任务页。</summary>
internal sealed class EnterpriseRequestDeliveryScanCursor
{
    private readonly object _gate = new();
    private Guid? _after;
    internal Guid? Read() { lock (_gate) return _after; }
    internal void Write(Guid? id) { lock (_gate) _after = id; }
}

/// <summary>按活动租户建立新作用域，恢复绑定或清理文件；不从 API 常规注册启动后台线程。</summary>
internal sealed partial class EnterpriseRequestDeliveryWorker(IServiceScopeFactory scopes,
    IOptions<EnterpriseRequestDeliveryOptions> options, EnterpriseRequestDeliveryScanCursor cursor,
    ILogger<EnterpriseRequestDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.IntervalSeconds, 1, 60)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(35));
                await RunBatchAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception e) { DeliveryFailed(logger, e); }
        }
    }

    internal async Task RunBatchAsync(CancellationToken token)
    {
        IReadOnlyList<Guid> tenantIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var context = sp.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            try
            {
                var p = EnterpriseRequestDeliverySql.Parameters(("Now", sp.GetRequiredService<IClock>().UtcNow), ("AfterTenantId", cursor.Read()));
                var statement = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider == DatabaseProvider.MySql
                    ? EnterpriseRequestDeliverySql.TenantScanMySql : EnterpriseRequestDeliverySql.TenantScanSqlServer;
                tenantIds = await sp.GetRequiredService<IQueryExecutor>().QueryAsync<Guid>(statement, p, token).ConfigureAwait(false);
                if (tenantIds.Count == 0 && cursor.Read() is not null)
                {
                    cursor.Write(null); p["AfterTenantId"] = null;
                    tenantIds = await sp.GetRequiredService<IQueryExecutor>().QueryAsync<Guid>(statement, p, token).ConfigureAwait(false);
                }
            }
            finally { context.Clear(); }
        }
        var processed = 0;
        foreach (var tenantId in tenantIds)
        {
            token.ThrowIfCancellationRequested();
            cursor.Write(tenantId);
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var context = sp.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            try
            {
                var active = await sp.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false);
                if (active is null) continue;
                context.SetTenant(active);
                var statement = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider == DatabaseProvider.MySql
                    ? EnterpriseRequestDeliverySql.DueMySql : EnterpriseRequestDeliverySql.DueSqlServer;
                var ids = await sp.GetRequiredService<IQueryExecutor>().QueryAsync<Guid>(statement,
                    EnterpriseRequestDeliverySql.Parameters(("Now", sp.GetRequiredService<IClock>().UtcNow)), token).ConfigureAwait(false);
                foreach (var id in ids)
                {
                    try { await sp.GetRequiredService<EnterpriseRequestDeliveryProcessor>().ProcessAsync(id, null, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { DeliveryFailed(logger, e); }
                    if (++processed >= 20) return;
                }
            }
            finally { context.Clear(); }
        }
    }
    [LoggerMessage(EventId = 7493, Level = LogLevel.Warning, Message = "企业申请交付恢复未完成，持久意图将在后续批次重试。")]
    private static partial void DeliveryFailed(ILogger logger, Exception exception);
}
