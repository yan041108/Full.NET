using Microsoft.Extensions.Options;
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
    ITenantResourceFileStorageUsagePort? storageUsage = null, IOptions<DatabaseOptions>? databaseOptions = null)
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

        if (request.AfterTenantId == Guid.Empty)
        {
            return Result<ReconcileTenantQuotaUsageBaselineResponse>.Failure(new Error(
                ValidationErrorCodes.Failed, "A continuation cursor must be an assigned tenant identifier.", ErrorType.Validation));
        }

        var candidateTenantIds = new HashSet<Guid>();
        var applied = 0;
        var now = clock.UtcNow;
        var defaultLimit = ResolveDefaultLimit(metricCode);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var parameters = TenancySqlParameters.Create(("LifecycleStatus", TenantLifecycleStatuses.Active),
            ("MetricCode", metricCode), ("PeriodKey", TenantQuotaDefaults.PeriodKey),
            ("DefaultLimit", defaultLimit), ("AfterTenantId", request.AfterTenantId), ("Take", pageSize + 1));
        var window = await queryExecutor.QueryAsync<TenantQuotaUsageBaselineRecord>(
            databaseOptions?.Value.Provider == DatabaseProvider.MySql
                ? TenantQuotaSql.ListBaselinePageMySql : TenantQuotaSql.ListBaselinePageSqlServer,
            parameters, cancellationToken).ConfigureAwait(false);
        var metrics = window.Take(pageSize).ToArray();
        var tenantIds = metrics.Where(metric => metric.ReservedValue == 0).Select(metric => metric.TenantId).Distinct().ToArray();
        var usages = string.Equals(metricCode, TenantQuotaMetricCodes.IdentitySeats, StringComparison.Ordinal)
            ? await activeMemberCount.CountActiveMembersByTenantsAsync(tenantIds, cancellationToken).ConfigureAwait(false)
            : await storageUsage!.SumReadyStorageBytesByTenantsAsync(tenantIds, cancellationToken).ConfigureAwait(false);
        foreach (var metric in metrics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (metric.ReservedValue != 0) continue;
            var authoritative = usages[metric.TenantId];
            var missing = metric.Id is null;
            if (!missing && metric.UsedValue == authoritative) continue;
            candidateTenantIds.Add(metric.TenantId);
            if (request.DryRun) continue;
            var affected = missing
                ? await commandExecutor.ExecuteAsync(TenantQuotaSql.InsertMetric,
                    TenancySqlParameters.Create(("Id", idGenerator.NewId()), ("TenantId", metric.TenantId),
                        ("MetricCode", metricCode), ("PeriodKey", TenantQuotaDefaults.PeriodKey),
                        ("LimitValue", defaultLimit), ("UsedValue", authoritative),
                        ("CreatedAtUtc", now), ("UpdatedAtUtc", now)), cancellationToken).ConfigureAwait(false)
                : await commandExecutor.ExecuteAsync(TenantQuotaSql.UpdateMetricUsedValue,
                    TenancySqlParameters.Create(("MetricId", metric.Id), ("UsedValue", authoritative),
                        ("UpdatedAtUtc", now), ("Version", metric.Version)), cancellationToken).ConfigureAwait(false);
            if (affected == 1) applied++;
        }

        return Result<ReconcileTenantQuotaUsageBaselineResponse>.Success(
            new ReconcileTenantQuotaUsageBaselineResponse(
                candidateTenantIds.Count,
                applied,
                request.DryRun,
                candidateTenantIds.ToList())
            { NextAfterTenantId = window.Count > pageSize ? metrics[^1].TenantId : null,
                IsComplete = window.Count <= pageSize });
    }

    private static long ResolveDefaultLimit(string metricCode) =>
        string.Equals(metricCode, TenantQuotaMetricCodes.FilesStorageBytes, StringComparison.Ordinal)
            ? TenantQuotaDefaults.FilesStorageBytesLimit
            : TenantQuotaDefaults.IdentitySeatsLimit;

    private static bool IsSupportedMetric(string metricCode) =>
        string.Equals(metricCode, TenantQuotaMetricCodes.IdentitySeats, StringComparison.Ordinal)
        || string.Equals(metricCode, TenantQuotaMetricCodes.FilesStorageBytes, StringComparison.Ordinal);
}
