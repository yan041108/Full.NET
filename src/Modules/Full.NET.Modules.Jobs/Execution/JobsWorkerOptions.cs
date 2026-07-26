using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>定义 Jobs Worker 的批大小与空轮询等待边界。</summary>
internal sealed class JobsWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Jobs:Worker";

    /// <summary>获取或设置每轮最多领取的任务数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>获取或设置两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 2000;

    /// <summary>获取或设置单次领取后租约的有效秒数。</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>获取或设置执行期间延长租约的间隔秒数。</summary>
    public int LeaseRenewalSeconds { get; set; } = 60;
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
