using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantQuota;

/// <summary>组合明确目标的账面与所有者计数，不能把跨模块观察当作写入或强一致快照。</summary>
internal sealed class TenantQuotaUsageDiagnosticService(IQueryExecutor queries, ITenantActiveMemberCountPort members,
    IClock clock, ITenantResourceFileStorageUsagePort? storage = null)
{
    internal static readonly SqlStatement Read = new("tenancy.quota.read_usage_diagnostic", """
        SELECT tenant.Id AS TenantId,tenant.IsActive AS TenantIsActive,metric.Id AS MetricId,metric.UsedValue,metric.ReservedValue,metric.Version
        FROM fn_tenancy_tenant tenant
        LEFT JOIN fn_tenancy_quota_metric metric ON metric.TenantId=tenant.Id
          AND metric.MetricCode=@MetricCode AND metric.PeriodKey=@PeriodKey
        WHERE tenant.Id=@TenantId
        """, SqlDataScope.HostOnly);

    internal async Task<Result<TenantQuotaUsageDiagnosticResponse>> GetAsync(Guid tenantId, string? metricCode, CancellationToken token)
    {
        if (tenantId == Guid.Empty) return Missing();
        var metric = metricCode?.Trim();
        if (metric is not (TenantQuotaMetricCodes.IdentitySeats or TenantQuotaMetricCodes.FilesStorageBytes))
            return Result<TenantQuotaUsageDiagnosticResponse>.Failure(new Error(TenancyErrorCodes.QuotaUsageBaselineUnsupportedMetric,
                "The requested quota metric does not support usage diagnostics.", ErrorType.Validation));
        var parameters = TenancySqlParameters.Create(("TenantId", tenantId), ("MetricCode", metric), ("PeriodKey", TenantQuotaDefaults.PeriodKey));
        var before = await queries.QuerySingleOrDefaultAsync<TenantQuotaDiagnosticSnapshot>(Read, parameters, token).ConfigureAwait(false);
        if (before is null || before.TenantId != tenantId) return Missing();
        Validate(before);
        Guid[] targets = [tenantId];
        var seats = metric == TenantQuotaMetricCodes.IdentitySeats;
        var unsettled = seats && (await members.FindUnsettledSeatTenantsAsync(targets, token).ConfigureAwait(false)).Contains(tenantId);
        IReadOnlyDictionary<Guid, long>? usages = seats
            ? await members.CountActiveMembersByTenantsAsync(targets, token).ConfigureAwait(false)
            : storage is null ? null : await storage.SumReadyStorageBytesByTenantsAsync(targets, token).ConfigureAwait(false);
        // 两次都执行，不能因第一次已发现工作项就短路掉第二次观察。
        if (seats) unsettled |= (await members.FindUnsettledSeatTenantsAsync(targets, token).ConfigureAwait(false)).Contains(tenantId);
        var after = await queries.QuerySingleOrDefaultAsync<TenantQuotaDiagnosticSnapshot>(Read, parameters, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (after is null || after.TenantId != tenantId) return Missing();
        Validate(after);
        long? authoritative = usages is not null && usages.TryGetValue(tenantId, out var count) && count >= 0 ? count : null;
        var state = authoritative is null ? "Unavailable"
            : unsettled || before != after || after.ReservedValue > 0 ? "InProgress"
            : after.MetricId is null ? "Unconfigured"
            : after.UsedValue == authoritative ? "Aligned" : "Different";
        var difference = state is "Aligned" or "Different" ? Format(authoritative!.Value - after.UsedValue!.Value) : null;
        return Result<TenantQuotaUsageDiagnosticResponse>.Success(new(tenantId, metric, after.TenantIsActive, after.MetricId,
            Format(after.UsedValue), Format(after.ReservedValue), Format(authoritative), difference, state, clock.UtcNow));
    }

    private static string? Format(long? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static void Validate(TenantQuotaDiagnosticSnapshot row)
    {
        if (row.MetricId is not null && (row.UsedValue is null or < 0 || row.ReservedValue is null or < 0 || row.Version is null or < 1))
            throw new InvalidOperationException("Incomplete tenant quota diagnostic projection.");
    }
    private static Result<TenantQuotaUsageDiagnosticResponse> Missing() => Result<TenantQuotaUsageDiagnosticResponse>.Failure(
        new Error(TenancyErrorCodes.NotFound, "Tenant was not found.", ErrorType.NotFound));
}

/// <summary>固定六列；空指标保留企业身份，不把缺失字段补成零。</summary>
internal sealed record TenantQuotaDiagnosticSnapshot(Guid TenantId, bool TenantIsActive, Guid? MetricId,
    long? UsedValue, long? ReservedValue, int? Version);
