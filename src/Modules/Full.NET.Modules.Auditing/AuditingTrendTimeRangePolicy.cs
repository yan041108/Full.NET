using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing;

/// <summary>趋势聚合查询的时间窗与桶数边界校验。</summary>
internal sealed class AuditingTrendTimeRangePolicy(
    IOptions<AuditingQueryOptions> options)
{
    /// <summary>
    /// 校验趋势查询时间范围，并在必要时自动选择桶宽。
    /// </summary>
    /// <param name="fromUtc">起始 UTC 时间（必填）。</param>
    /// <param name="toUtc">结束 UTC 时间（必填）。</param>
    /// <param name="requestedBucketMinutes">客户端请求的桶宽；为空时由服务端推导。</param>
    /// <returns>成功时返回计划；失败时返回验证错误。</returns>
    public Result<AuditLogTrendPlan> Plan(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? requestedBucketMinutes)
    {
        if (fromUtc is null || toUtc is null)
        {
            return Result<AuditLogTrendPlan>.Failure(new Error(
                AuditingErrorCodes.TrendTimeRangeRequired,
                "Trend queries require both fromUtc and toUtc.",
                ErrorType.Validation));
        }

        if (fromUtc > toUtc)
        {
            return Result<AuditLogTrendPlan>.Failure(new Error(
                AuditingErrorCodes.TimeRangeInvalid,
                "fromUtc must not be later than toUtc.",
                ErrorType.Validation));
        }

        if (toUtc.Value - fromUtc.Value
            > TimeSpan.FromDays(options.Value.MaximumTrendWindowDays))
        {
            return Result<AuditLogTrendPlan>.Failure(new Error(
                AuditingErrorCodes.TrendTimeRangeExceeded,
                "The trend query time range exceeds the configured maximum.",
                ErrorType.Validation));
        }

        return AuditLogTrendPlanner.Plan(
            fromUtc.Value,
            toUtc.Value,
            requestedBucketMinutes,
            options.Value.MaximumTrendBuckets);
    }
}

/// <summary>趋势查询执行计划。</summary>
internal readonly record struct AuditLogTrendPlan(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int BucketSizeMinutes,
    int ExpectedBucketCount);

/// <summary>根据时间窗与桶数上限推导合法桶宽。</summary>
internal static class AuditLogTrendPlanner
{
    private static readonly int[] AllowedBucketMinutes =
    [
        1, 5, 15, 30, 60, 120, 240, 360, 720, 1440,
    ];

    public static Result<AuditLogTrendPlan> Plan(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? requestedBucketMinutes,
        int maximumBuckets)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBuckets, 1);

        var totalMinutes = Math.Max(
            1,
            (int)Math.Ceiling((toUtc - fromUtc).TotalMinutes));

        int bucketSizeMinutes;
        if (requestedBucketMinutes is null)
        {
            bucketSizeMinutes = ResolveAutoBucketMinutes(totalMinutes, maximumBuckets);
        }
        else
        {
            bucketSizeMinutes = requestedBucketMinutes.Value;
            if (bucketSizeMinutes is < 1 or > 1440)
            {
                return Result<AuditLogTrendPlan>.Failure(new Error(
                    AuditingErrorCodes.TrendBucketSizeInvalid,
                    "bucketMinutes must be between 1 and 1440.",
                    ErrorType.Validation));
            }
        }

        var expectedBucketCount = (int)Math.Ceiling(totalMinutes / (double)bucketSizeMinutes);
        if (expectedBucketCount > maximumBuckets)
        {
            return Result<AuditLogTrendPlan>.Failure(new Error(
                AuditingErrorCodes.TrendBucketLimitExceeded,
                "The trend query would exceed the configured bucket limit.",
                ErrorType.Validation));
        }

        return Result<AuditLogTrendPlan>.Success(
            new AuditLogTrendPlan(fromUtc, toUtc, bucketSizeMinutes, expectedBucketCount));
    }

    private static int ResolveAutoBucketMinutes(int totalMinutes, int maximumBuckets)
    {
        var minimumBucketMinutes = (int)Math.Ceiling(totalMinutes / (double)maximumBuckets);
        foreach (var candidate in AllowedBucketMinutes)
        {
            if (candidate >= minimumBucketMinutes)
            {
                return candidate;
            }
        }

        return AllowedBucketMinutes[^1];
    }
}
