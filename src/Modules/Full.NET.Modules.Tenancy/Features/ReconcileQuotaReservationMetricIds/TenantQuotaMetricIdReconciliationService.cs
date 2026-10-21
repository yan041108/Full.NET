using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Persistence;

/// <summary>Tenancy 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Tenancy.Features.ReconcileQuotaReservationMetricIds;

internal sealed class TenantQuotaMetricIdReconciliationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock, IOptions<DatabaseOptions>? databaseOptions = null)
{
    public async Task<Result<ReconcileTenantQuotaMetricIdsResponse>> ReconcileAsync(
        ReconcileTenantQuotaMetricIdsRequest request,
        CancellationToken cancellationToken = default)
    {
        var dryRun = request.DryRun;
        var repaired = 0;
        var skipped = 0;
        var scanned = 0;
        Guid? afterId = null;
        while (true)
        {
            var outstanding = await queryExecutor.QueryAsync<OutstandingReservationRow>(
                databaseOptions?.Value.Provider == DatabaseProvider.MySql
                    ? TenantQuotaSql.ListReservationsWithNullMetricIdMySql : TenantQuotaSql.ListReservationsWithNullMetricId,
                TenancySqlParameters.Create(("AfterId", afterId)), cancellationToken).ConfigureAwait(false);
            scanned += outstanding.Count;
            if (outstanding.Count == 0) break;
            // 同一批次内同租户同指标只解析一次；缓存只活在这次有界修复页内。
            var metrics = new Dictionary<(Guid TenantId, string MetricCode), TenantQuotaMetricRecord?>();
        foreach (var row in outstanding)
        {
            if (!metrics.TryGetValue((row.TenantId, row.MetricCode), out var metric))
            {
                metric = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
                    TenantQuotaSql.FindMetric,
                    TenancySqlParameters.Create(
                        ("TenantId", row.TenantId),
                        ("MetricCode", row.MetricCode),
                        ("PeriodKey", TenantQuotaDefaults.PeriodKey)),
                    cancellationToken)
                .ConfigureAwait(false);
                metrics[(row.TenantId, row.MetricCode)] = metric;
            }
            if (metric is null)
            {
                skipped++;
                continue;
            }

            if (!dryRun)
            {
                var affected = await commandExecutor.ExecuteAsync(
                        TenantQuotaSql.UpdateReservationMetricId,
                        TenancySqlParameters.Create(
                            ("ReservationId", row.Id),
                            ("MetricId", metric.Id),
                            ("UpdatedAtUtc", clock.UtcNow),
                            ("Version", row.Version)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affected != 1)
                {
                    skipped++;
                    continue;
                }
            }

            repaired++;
        }

            afterId = outstanding[^1].Id;
            if (outstanding.Count < 500) break;
        }

        return Result<ReconcileTenantQuotaMetricIdsResponse>.Success(
            new ReconcileTenantQuotaMetricIdsResponse(
                scanned,
                repaired,
                skipped,
                dryRun));
    }

    private sealed record OutstandingReservationRow(
        Guid Id,
        Guid TenantId,
        string MetricCode,
        int Version);
}
