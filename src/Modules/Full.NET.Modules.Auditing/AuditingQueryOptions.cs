using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing;

/// <summary>定义审计查询中高成本 contains 筛选的服务端边界。</summary>
internal sealed class AuditingQueryOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Auditing:Query";

    /// <summary>获取或设置 contains 查询允许的最大闭区间天数。</summary>
    public int MaximumContainsWindowDays { get; set; } = 1;

    /// <summary>获取或设置趋势聚合查询允许的最大闭区间天数。</summary>
    public int MaximumTrendWindowDays { get; set; } = 7;

    /// <summary>获取或设置单次趋势响应允许的最大时间桶数量。</summary>
    public int MaximumTrendBuckets { get; set; } = 96;
}

internal sealed class AuditingQueryOptionsValidator
    : IValidateOptions<AuditingQueryOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AuditingQueryOptions options)
    {
        if (options.MaximumContainsWindowDays is < 1 or > 31)
        {
            return ValidateOptionsResult.Fail(
                "Auditing:Query:MaximumContainsWindowDays must be between 1 and 31.");
        }

        if (options.MaximumTrendWindowDays is < 1 or > 31)
        {
            return ValidateOptionsResult.Fail(
                "Auditing:Query:MaximumTrendWindowDays must be between 1 and 31.");
        }

        if (options.MaximumTrendBuckets is < 1 or > 168)
        {
            return ValidateOptionsResult.Fail(
                "Auditing:Query:MaximumTrendBuckets must be between 1 and 168.");
        }

        return ValidateOptionsResult.Success;
    }
}
