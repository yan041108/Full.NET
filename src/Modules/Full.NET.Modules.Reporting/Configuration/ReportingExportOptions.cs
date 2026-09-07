using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Configuration;

/// <summary>报表导出后台领取与租约配置。</summary>
public sealed class ReportingExportOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Reporting:Export";

    /// <summary>是否启用 Worker 崩溃恢复循环。</summary>
    public bool ExecutionEnabled { get; init; } = true;

    /// <summary>轮询间隔秒数，默认 15 秒。</summary>
    public int PollSeconds { get; init; } = 15;

    /// <summary>单次迭代最多处理的任务数。</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>执行租约秒数；长导出必须能在到期前完成或由请求内路径持有。</summary>
    public int LeaseSeconds { get; init; } = 300;
}

/// <summary>校验报表导出 Worker 配置边界。</summary>
internal sealed class ReportingExportOptionsValidator : IValidateOptions<ReportingExportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ReportingExportOptions options)
    {
        if (options.PollSeconds is < 5 or > 300)
        {
            return ValidateOptionsResult.Fail("报表导出轮询间隔必须在 5 到 300 秒之间。");
        }

        if (options.BatchSize is < 1 or > 200)
        {
            return ValidateOptionsResult.Fail("报表导出批大小必须在 1 到 200 之间。");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            return ValidateOptionsResult.Fail("报表导出租约必须在 30 到 3600 秒之间。");
        }

        return ValidateOptionsResult.Success;
    }
}
