using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantQuota;

/// <summary>仅通过 Host 查询边界读取两项正式指标，不播种、不修复、不扩大额度。</summary>
internal sealed class TenantQuotaUsageQueryService(IQueryExecutor queries, IClock clock)
{
    internal static readonly SqlStatement Read = new("tenancy.quota.read_usage_summary", """
        SELECT tenant.Id AS TenantId, tenant.IsActive AS TenantIsActive,
               metric.Id AS MetricId, metric.MetricCode, metric.PeriodKey,
               metric.LimitValue, metric.UsedValue, metric.ReservedValue
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_quota_metric AS metric ON metric.TenantId = tenant.Id
          AND metric.PeriodKey = @PeriodKey AND metric.MetricCode IN (@SeatsCode, @StorageCode)
        WHERE tenant.Id = @TenantId
        ORDER BY metric.MetricCode
        """, SqlDataScope.HostOnly);

    internal async Task<Result<TenantQuotaUsageSummaryResponse>> GetAsync(Guid tenantId, CancellationToken token)
    {
        var rows = await queries.QueryAsync<TenantQuotaUsageRow>(Read, Persistence.TenancySqlParameters.Create(
            ("TenantId", tenantId), ("PeriodKey", TenantQuotaDefaults.PeriodKey),
            ("SeatsCode", TenantQuotaMetricCodes.IdentitySeats), ("StorageCode", TenantQuotaMetricCodes.FilesStorageBytes)), token).ConfigureAwait(false);
        if (rows.Count == 0) return Result<TenantQuotaUsageSummaryResponse>.Failure(new Error(TenancyErrorCodes.NotFound, "Tenant was not found.", ErrorType.NotFound));
        var metrics = new List<TenantQuotaUsageItemResponse>(2);
        foreach (var row in rows)
        {
            if (row.MetricId is not { } id) continue;
            // LEFT JOIN 只允许整项缺失，不能把损坏的非空指标补成零用量。
            metrics.Add(new(id, row.MetricCode ?? throw Incomplete(), row.PeriodKey ?? throw Incomplete(),
                row.LimitValue ?? throw Incomplete(), row.UsedValue ?? throw Incomplete(), row.ReservedValue ?? throw Incomplete()));
        }
        return Result<TenantQuotaUsageSummaryResponse>.Success(new(rows[0].TenantId, rows[0].TenantIsActive, clock.UtcNow, metrics));
    }
    private static InvalidOperationException Incomplete() => new("Incomplete tenant quota usage projection.");
}

/// <summary>同模块 LEFT JOIN 的固定八列；企业存在但无指标时保留企业身份。</summary>
internal sealed record TenantQuotaUsageRow(Guid TenantId, bool TenantIsActive, Guid? MetricId, string? MetricCode, string? PeriodKey,
    long? LimitValue, long? UsedValue, long? ReservedValue);
