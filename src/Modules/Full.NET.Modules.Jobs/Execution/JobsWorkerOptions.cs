using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>定义 Jobs Worker 的批大小与空轮询等待边界。</summary>
internal sealed class JobsWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Jobs:Worker";

    /// <summary>获取或设置每轮最多领取的任务数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>获取或设置同一批次内允许并行执行的最大任务数量；默认保持串行。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>获取或设置两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 2000;

    /// <summary>获取或设置单次领取后租约的有效秒数。</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>获取或设置执行期间延长租约的间隔秒数。</summary>
    public int LeaseRenewalSeconds { get; set; } = 60;

    /// <summary>获取或设置单条执行允许的总尝试次数；默认一次以保持现有失败语义。</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>获取或设置显式可重试失败再次允许领取前的固定等待秒数。</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>获取或设置重试退避模式；默认固定延迟以保持现有部署行为。</summary>
    public string RetryBackoffMode { get; set; } = "fixed";

    /// <summary>获取或设置退避和抖动后的最大延迟秒数，避免失败任务无限延后。</summary>
    public int RetryMaxDelaySeconds { get; set; } = 86400;

    /// <summary>获取或设置对称抖动百分比；默认不抖动以保持现有调度结果。</summary>
    public int RetryJitterPercent { get; set; }

    /// <summary>获取或设置数据库积压快照的采样间隔秒数。</summary>
    public int BacklogSampleSeconds { get; set; } = 30;
}

internal sealed class JobsWorkerOptionsValidator : IValidateOptions<JobsWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("Jobs:Worker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add(
                "Jobs:Worker:PollMilliseconds must be between 100 and 60000.");
        }

        if (options.MaxConcurrency is < 1 or > 16)
        {
            failures.Add(
                "Jobs:Worker:MaxConcurrency must be between 1 and 16.");
        }

        if (options.MaxConcurrency > options.BatchSize)
        {
            failures.Add(
                "Jobs:Worker:MaxConcurrency must not exceed BatchSize.");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            failures.Add(
                "Jobs:Worker:LeaseSeconds must be between 30 and 3600.");
        }

        if (options.LeaseRenewalSeconds is < 5 or > 1200)
        {
            failures.Add(
                "Jobs:Worker:LeaseRenewalSeconds must be between 5 and 1200.");
        }

        if (options.LeaseRenewalSeconds > options.LeaseSeconds / 2)
        {
            failures.Add(
                "Jobs:Worker:LeaseRenewalSeconds must not exceed half of LeaseSeconds.");
        }

        if (options.MaxAttempts is < 1 or > 10)
        {
            failures.Add(
                "Jobs:Worker:MaxAttempts must be between 1 and 10.");
        }

        if (options.RetryDelaySeconds is < 1 or > 86400)
        {
            failures.Add(
                "Jobs:Worker:RetryDelaySeconds must be between 1 and 86400.");
        }

        if (!string.Equals(
                options.RetryBackoffMode,
                "fixed",
                StringComparison.Ordinal)
            && !string.Equals(
                options.RetryBackoffMode,
                "exponential",
                StringComparison.Ordinal))
        {
            failures.Add(
                "Jobs:Worker:RetryBackoffMode must be 'fixed' or 'exponential'.");
        }

        if (options.RetryMaxDelaySeconds is < 1 or > 86400)
        {
            failures.Add(
                "Jobs:Worker:RetryMaxDelaySeconds must be between 1 and 86400.");
        }

        if (options.RetryMaxDelaySeconds < options.RetryDelaySeconds)
        {
            failures.Add(
                "Jobs:Worker:RetryMaxDelaySeconds must not be less than RetryDelaySeconds.");
        }

        if (options.RetryJitterPercent is < 0 or > 50)
        {
            failures.Add(
                "Jobs:Worker:RetryJitterPercent must be between 0 and 50.");
        }

        if (options.BacklogSampleSeconds is < 5 or > 3600)
        {
            failures.Add(
                "Jobs:Worker:BacklogSampleSeconds must be between 5 and 3600.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
