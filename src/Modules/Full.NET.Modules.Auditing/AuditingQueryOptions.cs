using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing;

/// <summary>定义审计查询中高成本 contains 筛选的服务端边界。</summary>
internal sealed class AuditingQueryOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Auditing:Query";

    /// <summary>获取或设置 contains 查询允许的最大闭区间天数。</summary>
    public int MaximumContainsWindowDays { get; set; } = 1;
}

internal sealed class AuditingQueryOptionsValidator
    : IValidateOptions<AuditingQueryOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AuditingQueryOptions options) =>
        options.MaximumContainsWindowDays is >= 1 and <= 31
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Auditing:Query:MaximumContainsWindowDays must be between 1 and 31.");
}
