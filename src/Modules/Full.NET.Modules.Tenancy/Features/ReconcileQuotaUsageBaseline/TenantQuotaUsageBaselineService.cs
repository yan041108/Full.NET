using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ReconcileQuotaUsageBaseline;

/// <summary>将配额 UsedValue 对齐权威计数，并为存量租户补建缺失指标行（默认 Limit + 当前用量）。</summary>
internal sealed class TenantQuotaUsageBaselineService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ITenantActiveMemberCountPort activeMemberCount,
    IClock clock,
    IIdGenerator idGenerator,
    ITenantResourceFileStorageUsagePort? storageUsage = null)
{
    public async Task<Result<ReconcileTenantQuotaUsageBaselineResponse>> ReconcileAsync(
        ReconcileTenantQuotaUsageBaselineRequest request,
        CancellationToken cancellationToken = default)
    {
        var metricCode = string.IsNullOrWhiteSpace(request.MetricCode)
            ? TenantQuotaMetricCodes.IdentitySeats
            : request.MetricCode.Trim();
        // Files 为可选模块；缺少权威用量端口时拒绝对账，不能以零用量覆盖存量配额。
        if (!IsSupportedMetric(metricCode)
            || (string.Equals(metricCode, TenantQuotaMetricCodes.FilesStorageBytes, StringComparison.Ordinal)
                && storageUsage is null))
        {
            return Result<ReconcileTenantQuotaUsageBaselineResponse>.Failure(new Error(
                TenancyErrorCodes.QuotaUsageBaselineUnsupportedMetric,
                "The requested quota metric does not support usage baseline reconciliation.",
                ErrorType.Validation));
        }

        var candidateTenantIds = new HashSet<Guid>();
        var applied = 0;
        var now = clock.UtcNow;
        var defaultLimit = ResolveDefaultLimit(metricCode);

        var missingTenants = await queryExecutor.QueryAsync<TenantQuotaMetricBackfillCandidate>(
                TenantQuotaSql.ListActiveTenantsMissingQuotaMetric,
                TenancySqlParameters.Create(
                    ("LifecycleStatus", TenantLifecycleStatuses.Active),
                    ("MetricCode", metricCode),
                    ("PeriodKey", TenantQuotaDefaults.PeriodKey)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var missing in missingTenants)
        {
            candidateTenantIds.Add(missing.TenantId);
            if (request.DryRun)
            {
                continue;
            }

            var authoritative = await ResolveAuthoritativeUsageAsync(
                    metricCode,
                    missing.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            var inserted = await commandExecutor.ExecuteAsync(
                    TenantQuotaSql.InsertMetric,
                    TenancySqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("TenantId", missing.TenantId),
                        ("MetricCode", metricCode),
                        ("PeriodKey", TenantQuotaDefaults.PeriodKey),
                        ("LimitValue", defaultLimit),
                        ("UsedValue", authoritative),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (inserted == 1)
            {
                applied += 1;
            }
        }

        var metrics = await queryExecutor.QueryAsync<TenantQuotaMetricRecord>(
                TenantQuotaSql.ListMetricsByMetricCode,
                TenancySqlParameters.Create(
                    ("MetricCode", metricCode),
                    ("PeriodKey", TenantQuotaDefaults.PeriodKey)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var metric in metrics)
        {
            if (metric.ReservedValue != 0)
            {
                continue;
            }

            var authoritative = await ResolveAuthoritativeUsageAsync(
                    metricCode,
                    metric.TenantId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (metric.UsedValue == authoritative)
            {
                continue;
            }

            candidateTenantIds.Add(metric.TenantId);
            if (request.DryRun)
            {
                continue;
            }

            var affected = await commandExecutor.ExecuteAsync(
                    TenantQuotaSql.UpdateMetricUsedValue,
                    TenancySqlParameters.Create(
                        ("MetricId", metric.Id),
                        ("UsedValue", authoritative),
                        ("UpdatedAtUtc", now),
                        ("Version", metric.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 1)
            {
                applied += 1;
            }
        }

        return Result<ReconcileTenantQuotaUsageBaselineResponse>.Success(
            new ReconcileTenantQuotaUsageBaselineResponse(
                candidateTenantIds.Count,
                applied,
                request.DryRun,
                candidateTenantIds.ToList()));
    }

    private Task<long> ResolveAuthoritativeUsageAsync(
        string metricCode,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        string.Equals(metricCode, TenantQuotaMetricCodes.IdentitySeats, StringComparison.Ordinal)
            ? activeMemberCount.CountActiveMembersAsync(tenantId, cancellationToken)
            : storageUsage!.SumReadyStorageBytesAsync(tenantId, cancellationToken);

    private static long ResolveDefaultLimit(string metricCode) =>
        string.Equals(metricCode, TenantQuotaMetricCodes.FilesStorageBytes, StringComparison.Ordinal)
            ? TenantQuotaDefaults.FilesStorageBytesLimit
            : TenantQuotaDefaults.IdentitySeatsLimit;

    private static bool IsSupportedMetric(string metricCode) =>
        string.Equals(metricCode, TenantQuotaMetricCodes.IdentitySeats, StringComparison.Ordinal)
        || string.Equals(metricCode, TenantQuotaMetricCodes.FilesStorageBytes, StringComparison.Ordinal);
}
