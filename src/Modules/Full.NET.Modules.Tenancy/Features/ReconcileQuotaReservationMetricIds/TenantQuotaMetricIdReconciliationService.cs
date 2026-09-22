using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ReconcileQuotaReservationMetricIds;

internal sealed class TenantQuotaMetricIdReconciliationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    public async Task<Result<ReconcileTenantQuotaMetricIdsResponse>> ReconcileAsync(
        ReconcileTenantQuotaMetricIdsRequest request,
        CancellationToken cancellationToken = default)
    {
        var dryRun = request.DryRun;
        var outstanding = await queryExecutor.QueryAsync<OutstandingReservationRow>(
                TenantQuotaSql.ListReservationsWithNullMetricId,
                TenancySqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        var repaired = 0;
        var skipped = 0;
        foreach (var row in outstanding)
        {
            var metric = await queryExecutor.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(
                    TenantQuotaSql.FindMetric,
                    TenancySqlParameters.Create(
                        ("TenantId", row.TenantId),
                        ("MetricCode", row.MetricCode),
                        ("PeriodKey", TenantQuotaDefaults.PeriodKey)),
                    cancellationToken)
                .ConfigureAwait(false);
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

        return Result<ReconcileTenantQuotaMetricIdsResponse>.Success(
            new ReconcileTenantQuotaMetricIdsResponse(
                outstanding.Count,
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
